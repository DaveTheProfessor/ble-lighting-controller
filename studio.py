"""Pixel-exact content renderer shared by preview and Bluetooth uploads."""
import colorsys
from functools import lru_cache
import json
import math
import os
from pathlib import Path
import urllib.request
from PIL import Image, ImageDraw, ImageFont, ImageOps, ImageSequence

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT / 'assets'
EMOJI = ASSETS / 'emoji'
FONTS = ROOT / 'runtime/Lib/site-packages/pypixelcolor/fonts'

def rgb(value):
    value = str(value).lstrip('#')
    if len(value) != 6:
        raise ValueError('Colors must use six hexadecimal digits.')
    return tuple(int(value[i:i+2], 16) for i in (0, 2, 4))

def tokens(text):
    """Keep variation selectors, skin tones, flags and ZWJ emoji together."""
    out = []
    for char in text:
        code = ord(char)
        if out and (char in ('\ufe0f', '\ufe0e', '\u200d', '\u20e3') or out[-1].endswith('\u200d') or 0x1f3fb <= code <= 0x1f3ff):
            out[-1] += char
        elif out and 0x1f1e6 <= code <= 0x1f1ff and len(out[-1]) == 1 and 0x1f1e6 <= ord(out[-1]) <= 0x1f1ff:
            out[-1] += char
        else:
            out.append(char)
    return out

def emoji_key(token):
    return '-'.join(f'{ord(c):x}' for c in token if c not in ('\ufe0f', '\ufe0e'))

def is_emoji(token):
    return ord(token[0]) >= 0x1f000 or 0x2600 <= ord(token[0]) <= 0x27ff or '\u20e3' in token

@lru_cache(maxsize=256)
def emoji_image(token, size):
    EMOJI.mkdir(parents=True, exist_ok=True)
    path = EMOJI / (emoji_key(token) + '.png')
    if not path.exists():
        url = 'https://cdn.jsdelivr.net/gh/jdecked/twemoji@17.0.3/assets/72x72/' + path.name
        try:
            with urllib.request.urlopen(url, timeout=6) as response:
                data = response.read()
            from io import BytesIO
            image = Image.open(BytesIO(data)).convert('RGBA')
            image.save(path)
        except Exception as exc:
            raise ValueError(f'Emoji {token} is unavailable. Choose an emoji from the built-in palette or connect to the internet.') from exc
    with Image.open(path) as image:
        return image.convert('RGBA').resize((size, size), Image.Resampling.LANCZOS)

def text_strip(request, height):
    text = str(request.get('text', '')).replace('\r', '').replace('\n', '   ')
    if not 1 <= len(text) <= 500:
        raise ValueError('Enter a message of 1 to 500 characters.')
    size = min(height, max(6, int(request.get('font_size', 13))))
    font_name = request.get('font', 'Pixel')
    filename = {'Pixel': str(FONTS / 'unifont.otf'), 'Bold': os.path.join(os.environ.get('WINDIR', 'C:/Windows'), 'Fonts/arialbd.ttf'),
                'Regular': os.path.join(os.environ.get('WINDIR', 'C:/Windows'), 'Fonts/arial.ttf')}[font_name]
    font = ImageFont.truetype(filename, size)
    items = []
    fore = rgb(request.get('color', 'ffffff'))
    palette = request.get('text_palette', 'Solid')
    for index, token in enumerate(tokens(text)):
        if is_emoji(token):
            glyph = emoji_image(token, min(height, size + 2)).copy()
        else:
            box = font.getbbox(token)
            width = max(2, int(math.ceil(font.getlength(token))) + 1)
            glyph = Image.new('RGBA', (width, height))
            fill = fore
            if palette == 'Rainbow':
                fill = tuple(int(v * 255) for v in colorsys.hsv_to_rgb((index * .12) % 1, 1, 1))
            elif palette == 'Red / green':
                fill = (255, 40, 20) if index % 2 == 0 else (30, 255, 70)
            elif palette == 'Candy cane':
                fill = (255, 40, 40) if index % 2 == 0 else (255, 255, 255)
            ImageDraw.Draw(glyph).text((0, (height-(box[3]-box[1]))//2-box[1]), token, font=font, fill=fill)
        items.append(glyph)
    strip = Image.new('RGBA', (sum(im.width + 1 for im in items), height))
    x = 0
    for glyph in items:
        strip.alpha_composite(glyph, (x, (height-glyph.height)//2))
        x += glyph.width + 1
    return strip

def background(request, width, height, frame):
    first, second = rgb(request.get('bg_color', '000000')), rgb(request.get('bg_color2', '000030'))
    style = request.get('background', 'Solid')
    image = Image.new('RGB', (width, height), first)
    draw = ImageDraw.Draw(image)
    if style != 'Solid':
        for x in range(width):
            t = x / max(1, width-1)
            if style == 'Gradient':
                c = tuple(int(a*(1-t)+b*t) for a,b in zip(first,second))
            elif style == 'Stripes':
                c = first if ((x+frame)//5) % 2 == 0 else second
            else:
                c = tuple(int(v*90) for v in colorsys.hsv_to_rgb((t+frame/50) % 1, 1, 1))
            draw.line((x,0,x,height-1), fill=c)
    return image

def render(request, width=96, height=16, directory=None):
    directory = Path(directory or ROOT / 'renders')
    directory.mkdir(parents=True, exist_ok=True)
    mode = request.get('content', 'text')
    if mode == 'image':
        source = Path(request.get('image_path', ''))
        if not source.is_file():
            raise ValueError('Choose an image or GIF file first.')
        frames, durations = [], []
        with Image.open(source) as image:
            if getattr(image, 'n_frames', 1) > 300:
                raise ValueError('Choose a GIF with no more than 300 frames.')
            for frame in ImageSequence.Iterator(image):
                rgba = frame.convert('RGBA')
                base = Image.new('RGBA', rgba.size, rgb(request.get('bg_color', '000000')) + (255,))
                rgba = Image.alpha_composite(base, rgba).convert('RGB')
                if request.get('fit', 'fit') == 'crop':
                    output = ImageOps.fit(rgba, (width,height), method=Image.Resampling.LANCZOS)
                else:
                    resized = ImageOps.contain(rgba, (width,height), method=Image.Resampling.LANCZOS)
                    output = Image.new('RGB', (width,height), rgb(request.get('bg_color','000000')))
                    output.paste(resized, ((width-resized.width)//2,(height-resized.height)//2))
                frames.append(output)
                durations.append(max(20, min(10000, int(frame.info.get('duration',100)))))
    else:
        strip = text_strip(request, height)
        effect = request.get('effect', 'None')
        animation = request.get('animation', 'scroll_left')
        cycle = strip.width + min(24, width//4)
        step = max(1, math.ceil(cycle / 240))
        count = math.ceil(cycle/step) if animation in ('scroll_left','scroll_right') else (32 if animation != 'static' or effect != 'None' or request.get('background') in ('Stripes','Rainbow') else 1)
        frames, durations = [], []
        for i in range(count):
            image = background(request,width,height,i)
            draw = ImageDraw.Draw(image)
            for n in range(6):
                if effect == 'Snow':
                    x, y = (n*37 + i//3) % width, (n*7 + i) % height
                    draw.point((x,y), fill=(180,220,255))
                elif effect in ('Sparkle','Confetti'):
                    x,y = (n*29 + (i//4)*11) % width, (n*7 + (i//4)*3) % height
                    c = (255,220,120) if effect == 'Sparkle' else tuple(int(v*255) for v in colorsys.hsv_to_rgb((n*.17)%1,1,1))
                    draw.point((x,y),fill=c)
            if animation == 'scroll_left':
                x = -(i*step % cycle)
            elif animation == 'scroll_right':
                x = i*step % cycle
            else:
                x = (width-strip.width)//2
            layer = strip
            if animation == 'blink' and i%16 >= 8:
                layer = None
            elif animation == 'fade':
                layer = strip.copy()
                layer.putalpha(strip.getchannel('A').point(lambda a: int(a*(.2+.8*(1+math.sin(i*math.tau/32))/2))))
            if layer is not None:
                image.paste(layer,(x,0),layer)
                if animation == 'scroll_left':
                    image.paste(layer,(x+cycle,0),layer)
                elif animation == 'scroll_right':
                    image.paste(layer,(x-cycle,0),layer)
            frames.append(image)
            durations.append(max(30, 160-int(request.get('speed',60))*1.25))
    import uuid
    path = directory / (uuid.uuid4().hex + ('.gif' if len(frames)>1 else '.png'))
    if len(frames) == 1:
        frames[0].save(path)
    else:
        # Use one adaptive palette for the whole animation. A web-safe palette
        # turns dark gradients into harsh blocks (e.g. green 48 becomes 51).
        atlas = Image.new('RGB', (width, height*len(frames)))
        for i, frame in enumerate(frames):
            atlas.paste(frame,(0,i*height))
        palette = atlas.quantize(colors=256,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
        quantized = [f.quantize(palette=palette,dither=Image.Dither.NONE) for f in frames]
        quantized[0].save(path,save_all=True,append_images=quantized[1:],duration=[int(d) for d in durations],loop=0,optimize=True,disposal=2)
    return dict(path=str(path), width=width,height=height,frames=len(frames),bytes=path.stat().st_size)
