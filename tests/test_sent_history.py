import asyncio
import json
from pathlib import Path
import sys
import tempfile
from dataclasses import dataclass
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
import controller
from sent_history import record_success
from PIL import Image

@dataclass
class Info:
    width:int=96
    height:int=16

class FakeClient:
    def __init__(self,address):self.address=address;self._session=self
    async def connect(self):
        if self.address=='FAIL':raise RuntimeError('Synthetic connection failure')
    def get_device_info(self):return Info()
    async def send_image(self,*args,**kwargs):pass
    async def send_text(self,*args,**kwargs):pass
    async def disconnect(self):pass

async def test():
    with tempfile.TemporaryDirectory() as scratch:
        root=Path(scratch).resolve();source=root/'original.png'
        Image.new('RGB',(96,16),(10,20,30)).save(source)
        original=source.read_bytes()
        controller.ROOT=root;controller.AsyncClient=FakeClient;controller.SIGN_PAUSE_SECONDS=0
        request=dict(action='design',content='image',image_path=str(source),devices=[dict(address='OK',name='Test sign'),dict(address='FAIL',name='Unavailable')],prepared=dict(path=str(source),width=96,height=16,frames=1,bytes=len(original)))
        response=await controller.execute(request)
        assert response['results'][0]['ok'] and not response['results'][1]['ok']
        rows=json.loads((root/'history.json').read_text(encoding='utf-8'))
        assert len(rows)==1 and rows[0]['address']=='OK'
        path=root/rows[0]['preview']['path']
        Image.new('RGB',(96,16),(200,0,0)).save(source)
        assert path.read_bytes()==original,'History media must survive source edits'
        assert rows[0]['request']['image_path']==rows[0]['preview']['path']
        assert 'devices' not in rows[0]['request'] and 'prepared' not in rows[0]['request']
        assert rows[0]['sent_at'].endswith('+00:00')
        await controller.execute(dict(action='info',devices=[dict(address='OK',name='Test sign')]))
        assert len(json.loads((root/'history.json').read_text()))==1
        await controller.execute(dict(action='text',text='Latest text',devices=[dict(address='OK',name='Test sign')]))
        rows=json.loads((root/'history.json').read_text())
        assert len(rows)==2 and rows[0]['summary']=='Latest text'
        assert rows[1]['id']==response['results'][0]['history_id']
        (root/'history.json').write_text('broken')
        result=await controller.execute(dict(action='text',text='Delivered',devices=[dict(address='OK',name='Test sign')]))
        assert result['ok'] and 'history_warning' in result['results'][0]
        print('PASS: per-sign history, immutable images, newest-first order, failed-send exclusion, no info-command records, save-failure reporting. No Bluetooth hardware used.')

asyncio.run(test())
