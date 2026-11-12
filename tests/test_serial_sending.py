import asyncio,sys,tempfile,time,json
from dataclasses import dataclass
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import controller as c
@dataclass
class Info:width:int=96;height:int=16
active=0;maximum=0;events=[];created=[]
class Fake:
    stuck=False
    def __init__(self,address):self.address=address;self._session=self;self._client=self;self.is_connected=False;created.append(address)
    async def connect(self):
        global active,maximum
        assert active==0,'Another sign is still connected'
        self.is_connected=True;active+=1;maximum=max(maximum,active);events.append(('connect',time.monotonic()));await asyncio.sleep(.001)
    async def disconnect(self):
        global active
        if self.stuck:raise RuntimeError('Synthetic stuck connection')
        if self.is_connected:active-=1;self.is_connected=False;events.append(('disconnect',time.monotonic()))
    def get_device_info(self):return Info()
    async def send_text(self,*args,**kwargs):await asyncio.sleep(.001)
async def main():
    global active,maximum
    with tempfile.TemporaryDirectory() as temp:
        c.ROOT=Path(temp).resolve();c.AsyncClient=Fake;c.SIGN_PAUSE_SECONDS=.005
        devices=[dict(address=str(n),name='Sign '+str(n)) for n in range(5)]
        a=dict(action='text',text='Test',devices=devices,_progress_path=str(c.ROOT/'progress.json'))
        b=dict(action='text',text='Test',devices=[dict(address='background',name='Background job')])
        results=await asyncio.gather(c.execute(a),c.execute(b))
        assert all(r['ok'] for r in results) and maximum==1 and active==0
        for i,event in enumerate(events):
            if event[0]=='connect' and i:assert event[1]-events[i-1][1]>=.004
        p=json.loads((c.ROOT/'progress.json').read_text());assert len(p['completed'])==5
        h=json.loads((c.ROOT/'history.json').read_text());assert all('_progress_path' not in r['request'] for r in h)
        created.clear();Fake.stuck=True
        result=await c.execute(dict(action='info',devices=devices))
        assert created==['0'] and len(result['results'])==5
        assert result['results'][0]['connection_released'] is False
        assert all(row.get('skipped') for row in result['results'][1:])
        c._pending_client=None
    print('PASS: five signs plus a competing background request, maximum one active connection, enforced pauses, per-sign progress, and stop-on-disconnect-failure. No Bluetooth hardware used.')
asyncio.run(main())
