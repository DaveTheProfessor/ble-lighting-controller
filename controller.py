"""Local BLE controller. Request/response JSON is also usable from other programs."""
import argparse
import asyncio
from dataclasses import asdict
import json
from pathlib import Path
import sys
import msvcrt
import time

from bleak import BleakScanner
from bleak.exc import BleakError
from pypixelcolor import AsyncClient
from datetime import datetime
from studio import render
from sent_history import record_success

ROOT = Path(__file__).resolve().parent
SIGN_PAUSE_SECONDS = 4.0
_pending_client = None

def progress(request,message,results=None):
    path=request.get('_progress_path')
    if not path:return
    target=Path(path);temporary=target.with_suffix('.tmp')
    data=dict(message=message,completed=[dict(name=r.get('alias',r['name']),ok=r.get('ok',False)) for r in (results or [])])
    try:
        temporary.write_text(json.dumps(data),encoding='utf-8');temporary.replace(target)
    except OSError:pass

async def close_connection(client):
    session=client._session
    transport=getattr(session,'_client',None)
    error=None
    try:await asyncio.wait_for(session.disconnect(),12)
    except Exception as exc:error=exc
    if transport is not None:
        try:
            if transport.is_connected:await asyncio.wait_for(transport.disconnect(),12)
            if transport.is_connected:return False,'Bluetooth connection is still open.'
            return True,None
        except Exception as exc:return False,str(exc)
    return (False,str(error)) if error else (True,None)

async def execute(request):
    global _pending_client
    if request.get('action','').startswith('schedule_'):
        from sign_schedule import action
        return await action(request)
    if request.get('action') == 'preview':
        return await _execute(request)
    # One adapter operation across every app instance and command-line caller.
    # The OS releases this byte-range lock if a worker exits unexpectedly.
    with (ROOT / '.bluetooth.lock').open('a+b') as lock:
        lock.seek(0, 2)
        if lock.tell() == 0:
            lock.write(b'0')
            lock.flush()
        for attempt in range(1200):
            lock.seek(0)
            try:
                msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
                break
            except OSError:
                if attempt == 1199:
                    raise TimeoutError('Bluetooth is busy. Wait for the other operation to finish.')
                if attempt==0:progress(request,'Waiting for the active Bluetooth job to finish before starting these signs.')
                await asyncio.sleep(.5)
        try:
            recovery=ROOT/'.bluetooth-recovery.json'
            if recovery.exists():
                remaining=min(10,max(0,json.loads(recovery.read_text(encoding='utf-8'))['not_before']-time.time()))
                if remaining:
                    progress(request,'Waiting for Bluetooth to recover before opening another connection.')
                    await asyncio.sleep(remaining)
            if _pending_client is not None:
                released,warning=await close_connection(_pending_client)
                if not released:return dict(ok=False,error='A previous Bluetooth connection is still open. No additional signs were contacted. '+warning)
                _pending_client=None
            return await _execute(request)
        finally:
            # Keep the shared adapter lock during the radio settling period.
            # This also separates individual calls from the background scheduler.
            await asyncio.sleep(SIGN_PAUSE_SECONDS)
            lock.seek(0)
            msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)

async def _execute(request):
    global _pending_client
    action = request['action']
    if action.startswith('fairy_'):
        from hello_fairy import execute as execute_fairy
        return await execute_fairy(request)
    if action == 'preview':
        preview = render(request, int(request.get('width', 96)), int(request.get('height', 16)))
        return dict(ok=True, preview=preview)
    if action == 'scan':
        found = await BleakScanner.discover(timeout=12, return_adv=True)
        devices = []
        for device, adv in found.values():
            name = adv.local_name or device.name or ''
            if name.startswith('LED_BLE'):
                devices.append(dict(name=name, address=device.address, rssi=adv.rssi))
        old_path = ROOT / 'devices.json'
        old = {d['address']: d for d in json.loads(old_path.read_text(encoding='utf-8-sig'))} if old_path.exists() else {}
        for device in devices:
            for key in ('alias', 'width', 'height'):
                if key in old.get(device['address'], {}):
                    device[key] = old[device['address']][key]
        devices.sort(key=lambda d: d.get('alias', d['name']))
        (ROOT / 'devices.json').write_text(json.dumps(devices, indent=2), encoding='utf-8')
        return dict(ok=True, devices=devices, message=f'Found {len(devices)} LED signs.')
    targets = request.get('devices', [])
    if not targets:
        raise ValueError('Select at least one sign.')
    if action not in ('info', 'text', 'design', 'replace_design', 'brightness', 'on', 'off', 'clock', 'orientation', 'slot', 'scores', 'timer'):
        raise ValueError('Unknown action.')
    if action == 'text' and not 1 <= len(request.get('text', '')) <= 500:
        raise ValueError('Enter a message of 1 to 500 characters.')
    results = []
    for index,device in enumerate(targets):
        label=device.get('alias',device['name'])
        progress(request,f'Sign {index+1} of {len(targets)}: {label} — connecting',results)
        client = AsyncClient(device['address'])
        row = dict(name=device['name'], alias=label, address=device['address'])
        try:
            for attempt in range(2):
                try:
                    await asyncio.wait_for(client.connect(), 35)
                    break
                except (TimeoutError, BleakError):
                    released,warning=await close_connection(client)
                    if not released:raise RuntimeError('Could not close the failed connection: '+warning)
                    if attempt:
                        raise
                    await asyncio.sleep(SIGN_PAUSE_SECONDS)
                    client = AsyncClient(device['address'])
            row['info'] = asdict(client.get_device_info())
            progress(request,f'Sign {index+1} of {len(targets)}: {label} — sending',results)
            if action in ('design', 'replace_design'):
                info = client.get_device_info()
                prepared = request.get('prepared', {})
                if prepared.get('width') == info.width and prepared.get('height') == info.height and Path(prepared.get('path','')).is_file():
                    content = prepared
                else:
                    content = render(request, info.width, info.height)
                save_slot=1 if action=='replace_design' else int(request.get('save_slot',0))
                await asyncio.wait_for(client.send_image(content['path'], save_slot=save_slot), 240)
                row['uploaded']=True
                if save_slot>0:
                    progress(request,f'Sign {index+1} of {len(targets)}: {label} — selecting the new message',results)
                    await asyncio.wait_for(client.show_slot(save_slot),10)
                    row['selected_slot']=save_slot
                if action=='replace_design':
                    row['deleted_slots']=[]
                    for number in range(2,11):
                        await asyncio.wait_for(client.delete(number),10)
                        row['deleted_slots'].append(number)
                    await asyncio.wait_for(client.show_slot(1),10)
                    await asyncio.wait_for(client.set_power(True),10)
                row['render'] = content
            elif action == 'text':
                await asyncio.wait_for(client.send_text(
                    request['text'], color=request.get('color', 'ffffff'),
                    animation=request.get('animation', 'scroll_left'),
                    speed=int(request.get('speed', 60)), save_slot=0), 60)
            elif action == 'brightness':
                await asyncio.wait_for(client.set_brightness(int(request['brightness'])), 10)
            elif action in ('on', 'off'):
                await asyncio.wait_for(client.set_power(action == 'on'), 10)
            elif action == 'clock':
                now = datetime.now()
                await asyncio.wait_for(client.set_time(now.hour, now.minute, now.second), 10)
                await asyncio.wait_for(client.set_clock_mode(style=int(request.get('clock_style',1)),show_date=bool(request.get('show_date',True)),format_24=bool(request.get('format_24',False))), 10)
            elif action == 'orientation':
                await asyncio.wait_for(client.set_orientation(int(request.get('orientation',0))), 10)
            elif action == 'slot':
                await asyncio.wait_for(client.show_slot(int(request.get('slot',1))), 10)
            elif action == 'scores':
                await asyncio.wait_for(client.set_scores(int(request.get('score1',0)),int(request.get('score2',0))), 10)
            elif action == 'timer':
                await asyncio.wait_for(client.set_timer(request.get('timer_action','start')), 10)
            row['ok'] = True
        except Exception as exc:
            row.update(ok=False, error=f'{type(exc).__name__}: {exc}')
        finally:
            progress(request,f'Sign {index+1} of {len(targets)}: {label} — closing its connection',results)
            released,warning=await close_connection(client)
            row['connection_released']=released
            if not released:
                _pending_client=client
                (ROOT/'.bluetooth-recovery.json').write_text(json.dumps(dict(not_before=time.time()+10)),encoding='utf-8')
                row['connection_warning']='The connection did not close. Remaining signs were held back: '+warning
        if row.get('ok'):
            try:
                history_request=dict(request,action='design',save_slot=1,cleanup_presets=True) if action=='replace_design' else request
                entry = record_success(ROOT, history_request, row)
                if entry:
                    row['history_id'] = entry['id']
            except Exception as exc:
                row['history_warning'] = 'Sent successfully, but history could not be saved: ' + str(exc)
        results.append(row)
        if not released:
            for pending in targets[index+1:]:
                results.append(dict(name=pending['name'],address=pending['address'],ok=False,skipped=True,error='Not attempted because the previous Bluetooth connection did not close. Retry this sign separately.'))
            progress(request,'Stopped: the last Bluetooth connection has not closed.',results)
            break
        if index<len(targets)-1:
            progress(request,f'{label} finished. Waiting {SIGN_PAUSE_SECONDS:g} seconds before the next sign.',results)
            await asyncio.sleep(SIGN_PAUSE_SECONDS)
    progress(request,'Finished processing the selected signs.',results)
    return dict(ok=all(r['ok'] for r in results), results=results)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--request', required=True)
    parser.add_argument('--response', required=True)
    args = parser.parse_args()
    try:
        request = json.loads(Path(args.request).read_text(encoding='utf-8-sig'))
        request['_progress_path']=args.response+'.progress.json'
        response = asyncio.run(execute(request))
    except Exception as exc:
        response = dict(ok=False, error=f'{type(exc).__name__}: {exc}')
    Path(args.response).write_text(json.dumps(response, indent=2), encoding='utf-8')
    return 0 if response['ok'] else 1

if __name__ == '__main__':
    sys.exit(main())
