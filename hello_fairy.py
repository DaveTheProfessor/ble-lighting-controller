"""Hello Fairy BLE power/color/effect controls, with device status readback.

Wire-format reference: https://github.com/nzrutman/hello_fairy_ble
This module implements only the documented basic commands, not firmware/DIY writes.
"""
import asyncio
import colorsys
from datetime import datetime, timezone
import json
from pathlib import Path
from bleak import BleakClient, BleakScanner

ROOT=Path(__file__).resolve().parent
WRITE='49535343-8841-43f4-a8d4-ecbe34729bb3'
NOTIFY='49535343-1e4d-4bd9-ba61-23c647249616'
EFFECTS={'Christmas':18,'Candy cane':50,'Halloween':20,'Fireworks':17,'Valentine':46,'St. Patrick':47,'July 4th':39,'Snow day':54,'Red and gold':40,'Blue / white dissolve':41,'Blue sparkle':56,'White sparkle':57,'Blue with pink sparkle':8,'May day':48}

def packet(command,payload=b''):
    data=bytes((0xaa,command,len(payload)))+bytes(payload)
    return data+bytes((sum(data)&255,))

def color_packet(color,brightness):
    color=str(color).lstrip('#')
    if len(color)!=6:raise ValueError('Choose an RGB color.')
    rgb=tuple(int(color[i:i+2],16)/255 for i in (0,2,4))
    hue,saturation,_=colorsys.rgb_to_hsv(*rgb)
    brightness=int(brightness)
    if not 0<=brightness<=100:raise ValueError('Brightness must be 0-100%.')
    if max(rgb)==0:brightness=0
    return packet(3,bytes((1,))+(int(round(hue*360))%360).to_bytes(2,'big')+int(round(saturation*1000)).to_bytes(2,'big')+(brightness*10).to_bytes(2,'big'))

def effect_packet(effect,brightness):
    effect,brightness=int(effect),int(brightness)
    if not 1<=effect<=58:raise ValueError('Choose an effect number from 1 to 58.')
    if not 0<=brightness<=100:raise ValueError('Brightness must be 0-100%.')
    return packet(3,bytes((2,effect))+(brightness*10).to_bytes(2,'big'))

def decode(data,curtain=False):
    if len(data)<4 or data[0]!=0xaa or data[2]!=len(data)-4 or (sum(data[:-1])&255)!=data[-1]:return None
    command,p=data[1],data[3:-1]
    if not p:return dict(kind='ack',command=command)
    if command==0 and len(p)>=27:
        return dict(kind='info',firmware=p[:5].decode('ascii','replace'),model=p[5:20].split(b'\0')[0].decode('ascii','replace'),led_count=int.from_bytes(p[20:22],'big'),columns=p[22],rows=p[24],diy_type=p[26])
    if command==1 and len(p)>=5 and p[3] in (0,1):
        result=dict(kind='status',power=bool(p[3]),mode=p[4])
        if p[4]==1 and len(p)>=11:
            hue,saturation,brightness=(int.from_bytes(p[i:i+2],'big') for i in (5,7,9))
            if hue>360 or saturation>1000 or brightness>1000:return None
            result.update(hue=hue,saturation=saturation,brightness=brightness/10,color=''.join(f'{round(v*255):02x}' for v in colorsys.hsv_to_rgb(hue/360,saturation/1000,1)))
        elif p[4]==2 and len(p)>=8:
            effect=p[5];brightness=int.from_bytes(p[6:8],'big')/(1 if curtain else 10)
            result.update(effect=effect,effect_name=next((k for k,v in EFFECTS.items() if v==effect),'Effect '+str(effect)),brightness=brightness)
        return result
    return None

class FairyClient:
    def __init__(self,address):
        self.client=BleakClient(address,timeout=20)
        self.info=None;self.status=None
        self.info_event=asyncio.Event();self.status_event=asyncio.Event();self.ack_event=asyncio.Event();self.expected_ack=None
    def on_notify(self,sender,data):
        curtain=bool(self.info and (self.info['led_count'],self.info['columns'],self.info['rows'],self.info['diy_type'])==(400,20,20,3))
        message=decode(bytes(data),curtain)
        if not message:return
        if message['kind']=='info':self.info=message;self.info_event.set()
        elif message['kind']=='status':self.status=message;self.status_event.set()
        elif message['kind']=='ack' and message['command']==self.expected_ack:self.ack_event.set()
    async def connect(self):
        await asyncio.wait_for(self.client.connect(),35)
        if not self.client.services.get_characteristic(WRITE):raise RuntimeError('This device does not expose the Hello Fairy command service.')
        await self.client.start_notify(NOTIFY,self.on_notify)
        await self.query(0,self.info_event)
        await self.read_status()
    async def query(self,command,event):
        event.clear()
        await self.client.write_gatt_char(WRITE,packet(command),response=False)
        await asyncio.wait_for(event.wait(),5)
    async def read_status(self):
        await self.query(1,self.status_event)
        return self.status
    async def send(self,data):
        self.expected_ack=data[1];self.ack_event.clear()
        await self.client.write_gatt_char(WRITE,data,response=False)
        try:await asyncio.wait_for(self.ack_event.wait(),5)
        finally:self.expected_ack=None
    async def close(self):
        try:await asyncio.wait_for(self.client.disconnect(),6)
        except Exception:pass

async def execute(request):
    action=request['action']
    if action=='fairy_scan':
        found=await BleakScanner.discover(timeout=12,return_adv=True)
        old_path=ROOT/'fairy-devices.json'
        old={d['address']:d for d in json.loads(old_path.read_text(encoding='utf-8-sig'))} if old_path.exists() else {}
        devices=[]
        for device,adv in found.values():
            name=(adv.local_name or device.name or '').strip()
            if name.startswith('Hello Fairy'):
                row=dict(name=name,address=device.address,rssi=adv.rssi)
                for key in ('alias','model','led_count','columns','rows'):
                    if key in old.get(device.address,{}):row[key]=old[device.address][key]
                devices.append(row)
        devices.sort(key=lambda d:d['name'])
        old_path.write_text(json.dumps(devices,indent=2),encoding='utf-8')
        return dict(ok=True,devices=devices,message=f'Found {len(devices)} Hello Fairy sets.')
    if action not in ('fairy_status','fairy_on','fairy_off','fairy_color','fairy_effect','fairy_brightness'):raise ValueError('Unknown light action.')
    targets=request.get('fairy_devices',[])
    if not targets:raise ValueError('Select at least one light set in Fairy lights.')
    results=[]
    for index,device in enumerate(targets):
        if request.get('_cancel_path') and Path(request['_cancel_path']).exists():
            for pending in targets[index:]:results.append(dict(name=pending.get('alias',pending['name']),address=pending['address'],ok=False,cancelled=True,error='Cancelled before this light set was contacted.'))
            break
        row=dict(name=device.get('alias',device['name']),address=device['address'])
        connection=None
        try:
            for attempt in range(2):
                connection=FairyClient(device['address'])
                try:
                    await connection.connect()
                    break
                except Exception:
                    await connection.close()
                    if attempt:raise
                    await asyncio.sleep(1)
            row['before']=dict(connection.status)
            if action in ('fairy_on','fairy_off'):
                await connection.send(packet(2,bytes((int(action=='fairy_on'),))))
            elif action in ('fairy_color','fairy_effect','fairy_brightness'):
                brightness=int(request.get('fairy_brightness',30))
                if action=='fairy_color':command=color_packet(request.get('fairy_color','ffffff'),brightness)
                elif action=='fairy_effect':command=effect_packet(request.get('fairy_effect',18),brightness)
                elif connection.status.get('mode')==2:command=effect_packet(connection.status['effect'],brightness)
                elif connection.status.get('mode')==1:command=color_packet(connection.status['color'],brightness)
                else:raise ValueError('Choose a solid color or built-in effect before adjusting brightness in this mode.')
                if not connection.status.get('power'):
                    await connection.send(packet(2,b'\x01'));await asyncio.sleep(.1)
                await connection.send(command)
            # A fresh status response distinguishes acknowledgement from observed state.
            await connection.read_status()
            row.update(ok=True,info=connection.info,status=connection.status)
        except Exception as exc:
            row.update(ok=False,error=f'{type(exc).__name__}: {exc}')
        finally:
            if connection:await connection.close()
        results.append(row)
    state_path=ROOT/'fairy-state.json'
    states=json.loads(state_path.read_text(encoding='utf-8-sig')) if state_path.exists() else {}
    for row in results:
        if row['ok']:states[row['address']]=dict(row,checked_at=datetime.now(timezone.utc).isoformat(),last_action=action)
    state_path.write_text(json.dumps(states,indent=2),encoding='utf-8')
    return dict(ok=all(row['ok'] for row in results),results=results)
