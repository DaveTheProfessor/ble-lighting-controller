"""Local, Eastern-time sign schedules; deterministic background execution."""
import argparse
import asyncio
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone, time as wall_time
import json
import msvcrt
from pathlib import Path
import shutil
import sys
import uuid
from zoneinfo import ZoneInfo

ROOT=Path(__file__).resolve().parent
ZONE=ZoneInfo('America/New_York')
UTC=timezone.utc

def now_utc():return datetime.now(UTC)
def iso(value):return value.astimezone(UTC).isoformat()
def parse(value):return datetime.fromisoformat(value).astimezone(UTC)
def read(path,default):return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else default
def write(path,data):
    temporary=path.with_suffix(path.suffix+'.tmp')
    temporary.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    temporary.replace(path)

@contextmanager
def file_lock(path,blocking=True):
    with path.open('a+b') as handle:
        handle.seek(0,2)
        if not handle.tell():handle.write(b'0');handle.flush()
        handle.seek(0)
        msvcrt.locking(handle.fileno(),msvcrt.LK_LOCK if blocking else msvcrt.LK_NBLCK,1)
        try:yield
        finally:handle.seek(0);msvcrt.locking(handle.fileno(),msvcrt.LK_UNLCK,1)

def local_time(value):
    naive=datetime.fromisoformat(value)
    if naive.tzinfo is not None:raise ValueError('Enter an Eastern local date and time.')
    local=naive.replace(tzinfo=ZONE,fold=0)
    if local.astimezone(UTC).astimezone(ZONE).replace(tzinfo=None)!=naive:
        raise ValueError('This time does not exist because the clocks change. Choose another time.')
    return local

def occurrences(job):
    start=local_time(job['start_local'])
    end=datetime.fromisoformat(job['end_date']).date() if job['repeat']!='Once' else start.date()
    day=start.date()
    while day<=end:
        delta=(day-start.date()).days
        repeat=job['repeat']
        if repeat=='Once' and delta==0 or repeat=='Daily' or repeat=='Weekdays' and day.weekday()<5 or repeat=='Weekly' and delta%7==0:
            value=datetime.combine(day,start.timetz().replace(tzinfo=None)).replace(tzinfo=ZONE,fold=0)
            # Recurring times in a spring-forward gap are skipped, not shifted.
            if value.astimezone(UTC).astimezone(ZONE).replace(tzinfo=None)==value.replace(tzinfo=None):yield value.astimezone(UTC)
        day+=timedelta(days=1)

def summary(root=ROOT,now=None):
    now=now or now_utc();jobs=read(root/'schedules.json',[]);ledger=read(root/'schedule-runs.json',{})
    for job in jobs:
        future=[value for value in occurrences(job) if value>now and value>=parse(job['updated_at'])]
        job['next_run']=iso(future[0]) if job['enabled'] and future else None
        relevant=[r for r in ledger.values() if r['schedule_id']==job['id'] and r['revision']==job['revision'] and parse(r['due_at'])>=parse(job['updated_at'])]
        relevant.sort(key=lambda r:r['due_at'],reverse=True)
        states=[s['state'] for s in relevant[0]['targets'].values()] if relevant else []
        past_due=any(value<=now and value>=parse(job['updated_at']) for value in occurrences(job))
        job['last_state']='Paused' if not job['enabled'] else 'Needs review' if 'uncertain' in states else 'Retrying' if 'retry' in states else 'Failed' if 'failed' in states else 'Sending' if 'running' in states else 'Sent' if states and all(s=='sent' for s in states) else 'Skipped' if states and all(s=='skipped' for s in states) else 'Ready' if future else 'Due' if past_due and (not states or 'pending' in states) else 'Finished'
    jobs.sort(key=lambda j:j['next_run'] or '9999')
    heartbeat=read(root/'schedule-worker-status.json',{})
    return dict(ok=True,jobs=jobs,worker=heartbeat,time_zone='America/New_York')

def save_schedule(request,root=ROOT,now=None):
    from studio import render
    now=now or now_utc()
    with file_lock(root/'.schedule-data.lock'):
        jobs=read(root/'schedules.json',[])
        identifier=request.get('id') or uuid.uuid4().hex
        previous=next((j for j in jobs if j['id']==identifier),None)
        start=local_time(request['start_local']);repeat=request.get('repeat','Once')
        if repeat not in ('Once','Daily','Weekdays','Weekly'):raise ValueError('Choose a supported repeat pattern.')
        end=datetime.fromisoformat(request.get('end_date',start.date().isoformat())).date()
        if repeat=='Once':end=start.date()
        if end<start.date() or (end-start.date()).days>366:raise ValueError('Choose an end date within one year after the start date.')
        if previous is None and start.astimezone(UTC)<=now:raise ValueError('Choose a future send time.')
        title=str(request.get('title','')).strip()
        if not title or len(title)>100:raise ValueError('Give this update a title of 1-100 characters.')
        known={d['address']:d for d in read(root/'devices.json',[])}
        addresses=list(dict.fromkeys(request.get('addresses',[])))
        if not addresses or any(address not in known for address in addresses):raise ValueError('Choose at least one known LED sign.')
        devices=[known[a] for a in addresses]
        job=dict(id=identifier,revision=(previous['revision']+1 if previous else 1),title=title,start_local=start.replace(tzinfo=None).isoformat(timespec='minutes'),repeat=repeat,end_date=end.isoformat(),time_zone='America/New_York',enabled=True,updated_at=iso(now),devices=devices)
        future={iso(t) for t in occurrences(job) if t>now}
        if not future:raise ValueError('This entry has no future send times. Adjust the start or end date.')
        for other in jobs:
            if other['id']==identifier or not other['enabled']:continue
            common=set(addresses)&{d['address'] for d in other['devices']}
            if common and future&{iso(t) for t in occurrences(other) if t>now}:
                raise ValueError('Another enabled entry already sends to one of these signs at the same time: '+other['title'])
        if previous and request.get('retain_design'):
            design=dict(previous['design']);previews=dict(previous['previews'])
        else:
            design=dict(request.get('design',{}));previews={}
            design={k:v for k,v in design.items() if k not in ('action','devices','prepared','save_slot')}
            if design.get('content')=='image':
                source=Path(design.get('image_path',''))
                if not source.is_absolute():source=root/source
                if not source.is_file():raise ValueError('The selected design image is missing.')
                directory=root/'schedule-media';directory.mkdir(exist_ok=True)
                copy=directory/(uuid.uuid4().hex+source.suffix.lower());shutil.copyfile(source,copy)
                design['image_path']=str(copy.relative_to(root))
        for device in devices:
            width,height=int(device.get('width',96)),int(device.get('height',16));key=f'{width}x{height}'
            if key not in previews:
                rendering=dict(design)
                if rendering.get('content')=='image':rendering['image_path']=str(root/rendering['image_path'])
                media=render(rendering,width,height,root/'schedule-media')
                media['path']=str(Path(media['path']).relative_to(root));previews[key]=media
        job.update(design=design,previews=previews)
        jobs=[j for j in jobs if j['id']!=identifier]+[job]
        write(root/'schedules.json',jobs)
    return dict(summary(root,now),saved_id=identifier,message='Schedule saved. Its design is frozen independently of later editor changes.')

def toggle_schedule(identifier,root=ROOT):
    with file_lock(root/'.schedule-data.lock'):
        jobs=read(root/'schedules.json',[]);job=next((j for j in jobs if j['id']==identifier),None)
        if not job:raise ValueError('Select a schedule entry first.')
        job['enabled']=not job['enabled']
        # Resuming starts with future occurrences; it does not replay paused ones.
        if job['enabled']:
            now=now_utc();future={iso(t) for t in occurrences(job) if t>now};addresses={d['address'] for d in job['devices']}
            for other in jobs:
                if other['id']!=identifier and other['enabled'] and addresses&{d['address'] for d in other['devices']} and future&{iso(t) for t in occurrences(other) if t>now}:
                    raise ValueError('Cannot resume: an enabled entry already uses one of these sign/time combinations: '+other['title'])
            job['updated_at']=iso(now)
        write(root/'schedules.json',jobs)
    return summary(root)

def confirmed_in_history(root,key,address):
    return any(r.get('address')==address and r.get('request',{}).get('scheduled_occurrence')==key for r in read(root/'history.json',[]))

async def run_due(root=ROOT,now=None,sender=None):
    now=now or now_utc()
    if sender is None:
        from controller import execute
        sender=execute
    lock=file_lock(root/'.schedule-runner.lock',False)
    try:lock.__enter__()
    except OSError as exc:
        if getattr(exc,'winerror',None) in (33,36) or exc.errno in (13,36):return dict(ok=True,message='Another sender is already running.')
        raise
    try:return await _run_due(root,now,sender)
    finally:lock.__exit__(None,None,None)

async def _run_due(root,now,sender):
    write(root/'schedule-worker-status.json',dict(started_at=iso(now),state='Checking'))
    jobs=read(root/'schedules.json',[]);ledger=read(root/'schedule-runs.json',{})
    due=[];latest={}
    for job in jobs:
        if not job['enabled']:continue
        for value in occurrences(job):
            if value>now or value<parse(job['updated_at']):continue
            key=f"{job['id']}:{job['revision']}:{iso(value)}"
            due.append((value,key,job))
            for device in job['devices']:
                address=device['address']
                if address not in latest or value>latest[address][0]:latest[address]=(value,key)
    attempts=0
    for when,key,job in sorted(due,key=lambda item:item[0]):
        record=ledger.setdefault(key,dict(schedule_id=job['id'],revision=job['revision'],title=job['title'],due_at=iso(when),targets={}))
        for target in job['devices']:record['targets'].setdefault(target['address'],dict(state='pending',attempts=0))
        for device in job['devices']:
            address=device['address'];state=record['targets'].setdefault(address,dict(state='pending',attempts=0))
            if state['state'] in ('sent','skipped','uncertain','failed'):continue
            if state['state']=='running':
                state['state']='sent' if confirmed_in_history(root,key,address) else 'uncertain'
                state['message']='Recovered confirmed delivery from history.' if state['state']=='sent' else 'Sender stopped during transfer. Check the sign before scheduling another send.'
                continue
            if now-when>timedelta(hours=24) or latest[address][1]!=key:
                state.update(state='skipped',message='Superseded by a newer update or more than 24 hours late.');continue
            if state.get('retry_at') and parse(state['retry_at'])>now:continue
            current=next((j for j in read(root/'schedules.json',[]) if j['id']==job['id']),None)
            if not current or not current['enabled'] or current['revision']!=job['revision'] or when<parse(current['updated_at']):continue
            state.update(state='running',attempts=state['attempts']+1,started_at=iso(now_utc()))
            write(root/'schedule-runs.json',ledger)
            request=dict(job['design'],action='design',devices=[device],save_slot=1,source='schedule',schedule_id=job['id'],scheduled_occurrence=key)
            if request.get('content')=='image':request['image_path']=str(root/request['image_path'])
            width,height=int(device.get('width',96)),int(device.get('height',16))
            request['prepared']=dict(job['previews'][f'{width}x{height}'])
            request['prepared']['path']=str(root/request['prepared']['path'])
            try:
                result=await sender(request);attempts+=1
                row=result.get('results',[{}])[0]
                if not row.get('ok'):raise RuntimeError(row.get('error',result.get('error','No delivery confirmation.')))
                state.update(state='sent',finished_at=iso(now_utc()),message='Successfully sent.')
                if row.get('connection_released') is False:
                    state['message']='Sent, but the Bluetooth connection did not close. Remaining signs held until the next sender run.'
                    write(root/'schedule-runs.json',ledger)
                    status=dict(started_at=iso(now),finished_at=iso(now_utc()),state='Waiting for Bluetooth recovery',sends_attempted=attempts)
                    write(root/'schedule-worker-status.json',status)
                    return dict(ok=False,**status)
            except Exception as exc:
                state.update(state='retry' if state['attempts']<3 else 'failed',message=f'{type(exc).__name__}: {exc}',retry_at=iso(now+timedelta(minutes=state['attempts'])))
            write(root/'schedule-runs.json',ledger)
    write(root/'schedule-runs.json',ledger)
    status=dict(started_at=iso(now),finished_at=iso(now_utc()),state='Ready',sends_attempted=attempts)
    write(root/'schedule-worker-status.json',status)
    return dict(ok=True,**status)

async def action(request):
    name=request['action']
    if name=='schedule_list':return summary()
    if name=='schedule_save':return save_schedule(request)
    if name=='schedule_toggle':return toggle_schedule(request.get('id'))
    if name=='schedule_enable':
        import subprocess,os
        shell=Path(os.environ['WINDIR'])/'System32/WindowsPowerShell/v1.0/powershell.exe'
        result=await asyncio.to_thread(subprocess.run,[str(shell),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'install-scheduler.ps1')],capture_output=True,text=True,creationflags=subprocess.CREATE_NO_WINDOW,timeout=45)
        if result.returncode:raise RuntimeError(result.stderr.strip() or result.stdout.strip() or 'Could not enable background sending.')
        return dict(summary(),message='Background sender enabled.')
    raise ValueError('Unknown schedule action.')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--run-due',action='store_true');parser.parse_args()
    try:asyncio.run(run_due())
    except Exception as exc:
        write(ROOT/'schedule-worker-status.json',dict(finished_at=iso(now_utc()),state='Error',error=f'{type(exc).__name__}: {exc}'))
        return 1
    return 0

if __name__=='__main__':sys.exit(main())
