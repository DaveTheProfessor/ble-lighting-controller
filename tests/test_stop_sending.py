import asyncio,json,sys,tempfile
from pathlib import Path
from dataclasses import dataclass
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import controller as c
@dataclass
class Info:width:int=96;height:int=16
created=[];cancel_path=None;stop_on_connect=False
class Fake:
    def __init__(self,address):self.address=address;self._session=self;created.append(address)
    async def connect(self):
        if stop_on_connect:cancel_path.write_text('stop')
    async def disconnect(self):pass
    def get_device_info(self):return Info()
    async def send_text(self,*args,**kwargs):cancel_path.write_text('stop')
async def main():
    global cancel_path,stop_on_connect
    with tempfile.TemporaryDirectory() as temp:
        c.ROOT=Path(temp).resolve();c.AsyncClient=Fake;c.SIGN_PAUSE_SECONDS=0
        cancel_path=c.ROOT/'cancel'
        devices=[dict(name='Test '+str(n),address=str(n)) for n in range(5)]
        request=dict(action='text',text='Test',devices=devices,_cancel_path=str(cancel_path))
        result=await c.execute(request)
        assert result['cancelled'] and created==['0'] and len(result['results'])==5
        assert result['results'][0]['ok'] and all(r['cancelled'] for r in result['results'][1:])
        assert len(json.loads((c.ROOT/'history.json').read_text()))==1
        created.clear();cancel_path.unlink()
        stop_on_connect=True
        result=await c.execute(request)
        assert result['cancelled'] and created==['0'] and len(result['results'])==5
        assert all(r['cancelled'] for r in result['results'])
        assert len(json.loads((c.ROOT/'history.json').read_text()))==1
        created.clear()
        result=await c.execute(request)
        assert result['cancelled'] and not created and len(result['results'])==5
    print('PASS: stop before start, stop after connection, finish active send then cancel all remaining signs; cancelled signs are not marked delivered.')
asyncio.run(main())
