import asyncio
from datetime import datetime,timedelta,timezone
import json
from pathlib import Path
import sys,tempfile
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import sign_schedule as s
from PIL import Image

CREATED=datetime(2026,11,12,17,tzinfo=timezone.utc)
DUE=datetime(2026,11,13,14,tzinfo=timezone.utc)
DEVICES=[dict(name='Test A',address='A',width=96,height=16),dict(name='Test B',address='B',width=192,height=16)]
def setup(root):s.write(root/'devices.json',DEVICES)
def add(root,time='2026-11-13T09:00',addresses=('A',),**extra):
    request=dict(title='Test update',start_local=time,end_date='2026-11-26',repeat='Once',addresses=list(addresses),design=dict(content='text',text='HELLO',font='Bold',font_size=12,animation='static',bg_color='000000'))
    request.update(extra)
    result=s.save_schedule(request,root,CREATED)
    return next(j for j in result['jobs'] if j['title']==request['title'])

async def main():
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp).resolve();setup(root);job=add(root)
        calls=[]
        async def send(request):
            calls.append(request)
            assert Path(request['prepared']['path']).is_file()
            return dict(ok=True,results=[dict(ok=True,address=request['devices'][0]['address'])])
        await s.run_due(root,DUE,send);await s.run_due(root,DUE+timedelta(minutes=2),send)
        assert len(calls)==1,'A completed occurrence must not be resent'
        assert calls[0]['scheduled_occurrence'] and calls[0]['source']=='schedule'
        assert calls[0]['save_slot']==1
        try:add(root,title='Conflicting update')
        except ValueError as exc:assert 'same time' in str(exc)
        else:raise AssertionError('Conflicting times must be rejected')
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp).resolve();setup(root);add(root,addresses=('A','B'))
        calls=[];fail=True
        async def send(request):
            a=request['devices'][0]['address'];calls.append(a)
            return dict(results=[dict(ok=a!='B' or not fail,error='Synthetic failure')])
        await s.run_due(root,DUE,send);await s.run_due(root,DUE+timedelta(seconds=30),send)
        fail=False;await s.run_due(root,DUE+timedelta(minutes=2),send)
        assert calls==['A','B','B'],calls
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp).resolve();setup(root);add(root,title='Older');add(root,time='2026-11-13T10:00',title='Latest')
        calls=[]
        async def send(request):calls.append(request);return dict(results=[dict(ok=True)])
        await s.run_due(root,DUE+timedelta(hours=2),send)
        assert len(calls)==1
        states=[v['targets']['A']['state'] for v in s.read(root/'schedule-runs.json',{}).values()]
        assert sorted(states)==['sent','skipped']
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp).resolve();setup(root);add(root,addresses=('A','B'));calls=[]
        async def stuck(request):calls.append(request['devices'][0]['address']);return dict(results=[dict(ok=True,connection_released=False)])
        await s.run_due(root,DUE,stuck)
        assert calls==['A']
        assert s.summary(root,DUE)['jobs'][0]['last_state']=='Due'
        async def recovered(request):calls.append(request['devices'][0]['address']);return dict(results=[dict(ok=True,connection_released=True)])
        await s.run_due(root,DUE+timedelta(minutes=1),recovered)
        assert calls==['A','B']
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp).resolve();setup(root);job=add(root)
        key=f"{job['id']}:1:{s.iso(DUE)}"
        s.write(root/'schedule-runs.json',{key:dict(schedule_id=job['id'],revision=1,title='Test',due_at=s.iso(DUE),targets={'A':dict(state='running',attempts=1)})})
        async def forbidden(request):raise AssertionError('An uncertain transfer must not automatically repeat')
        await s.run_due(root,DUE+timedelta(minutes=1),forbidden)
        assert s.read(root/'schedule-runs.json',{})[key]['targets']['A']['state']=='uncertain'
        ledger=s.read(root/'schedule-runs.json',{});ledger[key]['targets']['A']['state']='running';s.write(root/'schedule-runs.json',ledger)
        s.write(root/'history.json',[dict(address='A',request=dict(scheduled_occurrence=key))])
        await s.run_due(root,DUE+timedelta(minutes=2),forbidden)
        assert s.read(root/'schedule-runs.json',{})[key]['targets']['A']['state']=='sent'
    with tempfile.TemporaryDirectory() as temp:
        root=Path(temp).resolve();setup(root);source=root/'source.png';Image.new('RGB',(96,16),'red').save(source)
        job=add(root,design=dict(content='image',image_path=str(source)))
        snapshot=root/job['previews']['96x16']['path'];before=snapshot.read_bytes();Image.new('RGB',(96,16),'blue').save(source)
        assert snapshot.read_bytes()==before
        job=add(root,time='2026-11-14T09:00',title='Weekdays',repeat='Weekdays',end_date='2026-11-19')
        days=[t.astimezone(s.ZONE).weekday() for t in s.occurrences(job)]
        assert days==[0,1,2,3],days
    try:s.local_time('2027-03-14T02:30')
    except ValueError:pass
    else:raise AssertionError('A nonexistent Eastern time must be rejected')
    job=dict(start_local='2026-10-31T09:00',repeat='Daily',end_date='2026-11-02')
    assert [t.hour for t in s.occurrences(job)]==[13,14,14]
    print('PASS: schedules, conflicts, immutable designs, repeat weekdays, Eastern DST, partial retries, missed-update catch-up, duplicate prevention and crash reconciliation. No Bluetooth sends performed.')

asyncio.run(main())
