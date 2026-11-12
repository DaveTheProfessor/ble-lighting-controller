"""Keep successful display sends and immutable copies of their rendered media."""
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import uuid

DISPLAY_ACTIONS = {'design', 'text', 'clock', 'scores', 'timer', 'slot'}

def record_success(root, request, result):
    root = Path(root)
    if not result.get('ok') or request.get('action') not in DISPLAY_ACTIONS:
        return None
    directory = root / 'history'
    directory.mkdir(parents=True, exist_ok=True)
    identifier = uuid.uuid4().hex
    design = {k:v for k,v in request.items() if k not in ('devices', 'prepared', 'action') and not k.startswith('_')}
    entry = dict(id=identifier, sent_at=datetime.now(timezone.utc).isoformat(),
                 address=result['address'], name=result['name'], action=request['action'], request=design)
    media = result.get('render')
    if media:
        original = Path(media['path'])
        saved = directory / (identifier + original.suffix.lower())
        shutil.copyfile(original, saved)
        entry['preview'] = dict(media, path=str(saved.relative_to(root)))
        design['width'], design['height'] = media['width'], media['height']
        if design.get('content') == 'image':
            # Reload the pixels actually sent even if the source is edited later.
            design['image_path'] = str(saved.relative_to(root))
    action = request['action']
    if action in ('text', 'design'):
        entry['summary'] = str(request.get('text', 'Image / GIF')) if request.get('content', 'text') != 'image' else 'Image / GIF'
    elif action == 'clock':
        entry['summary'] = 'Clock, face ' + str(request.get('clock_style', 1))
    elif action == 'scores':
        entry['summary'] = f"Scoreboard: {request.get('score1', 0)} - {request.get('score2', 0)}"
    elif action == 'slot':
        entry['summary'] = 'Show preset ' + str(request.get('slot', 1))
    else:
        entry['summary'] = 'Stopwatch: ' + str(request.get('timer_action', 'start'))
    path = root / 'history.json'
    previous = json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else []
    previous.insert(0, entry)
    temporary = root / 'history.json.tmp'
    temporary.write_text(json.dumps(previous, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(path)
    return entry
