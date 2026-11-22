import sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio import render,text_strip,scroll_plan,TEXT_FRAME_LIMIT,RENDERER_VERSION
from PIL import Image

for width in (96,192):
    for length in (20,80,200,500):
        design=dict(content='text',text='W'*length,font='Bold',font_size=14,text_palette='Candy cane',background='Solid',bg_color='000000',animation='scroll_left',speed=60,effect='None')
        strip=text_strip(design,16);positions,duration,cycle=scroll_plan(strip.width,width,60)
        assert len(positions)<=TEXT_FRAME_LIMIT and positions[0]==0
        assert positions[-1]>strip.width-width,'The tail must enter the visible area before looping'
        # Every position in the message passes through the viewport at least once.
        covered=set()
        for position in positions:covered.update(range(position,min(strip.width,position+width)))
        assert len(covered)==strip.width,(width,length,strip.width)
with tempfile.TemporaryDirectory() as temp:
    design.update(text='BEGIN - '+('read the entire message '*5)+' - END',font_size=13)
    result=render(design,96,16,temp)
    assert result['renderer_version']==RENDERER_VERSION and result['frames']<=TEXT_FRAME_LIMIT
    with Image.open(result['path']) as image:
        assert image.n_frames<=TEXT_FRAME_LIMIT and image.info['loop']==0
print('PASS: complete scroll coverage including message endings for 20/80/200/500-character messages on both sign sizes; compact GIF frames and renderer version.')
