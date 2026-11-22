import asyncio,json,sys,tempfile
from pathlib import Path
from dataclasses import dataclass
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import controller as c
from PIL import Image
@dataclass
class Info:width:int=96;height:int=16
calls=[]
class Fake:
    fail=False
    def __init__(self,address):self._session=self
    async def connect(self):pass
    async def disconnect(self):pass
    def get_device_info(self):return Info()
    async def send_image(self,path,save_slot):
        calls.append(('upload',save_slot))
        if self.fail:raise RuntimeError('Synthetic transfer failure')
    async def show_slot(self,number):calls.append(('show',number))
    async def delete(self,number):calls.append(('delete',number))
    async def set_power(self,on):calls.append(('power',on))
async def main():
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp).resolve();c.ROOT=root;c.AsyncClient=Fake;c.SIGN_PAUSE_SECONDS=0
        path=root/'design.png';Image.new('RGB',(96,16),'red').save(path)
        request=dict(action='design',devices=[dict(address='TEST',name='Test')],save_slot=3,content='text',text='Test',prepared=dict(path=str(path),width=96,height=16,frames=1,bytes=path.stat().st_size,renderer_version=c.RENDERER_VERSION))
        result=await c.execute(request);assert result['ok']
        assert calls==[('upload',3),('show',3)],calls
        calls.clear();request['action']='replace_design'
        result=await c.execute(request);assert result['ok']
        assert calls==[('upload',1),('show',1)]+[('delete',n) for n in range(2,11)]+[('show',1),('power',True)],calls
        calls.clear();Fake.fail=True
        result=await c.execute(request);assert not result['ok']
        assert calls==[('upload',1)],'No other slots should be removed after a failed upload'
        records=json.loads((root/'history.json').read_text());assert len(records)==2 and records[0]['request']['cleanup_presets']
    print('PASS: saved-slot activation, preset cleanup order, no deletion after a failed upload, accurate history.')
asyncio.run(main())
