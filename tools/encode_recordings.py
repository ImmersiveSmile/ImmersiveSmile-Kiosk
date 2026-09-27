#!/usr/bin/env python3
"""Encode completed Unity captures and publish the kiosk media manifest.
Run from the Unity project root. Uses ffmpeg; never changes Unity source scenes.
"""
import json, pathlib, re, subprocess, sys, time, shutil
ROOT=pathlib.Path(__file__).resolve().parents[3]
CAPTURE=ROOT/'Temp/KioskRecordings'
REPORT=ROOT/'Web/Kiosk/recordings'
OUTPUT=ROOT/'Web/Kiosk/assets/recordings'
OUTPUT.mkdir(parents=True,exist_ok=True)
manifest={}
manifest_path=ROOT/'Web/Kiosk/recordings.js'
if manifest_path.exists():
    try: manifest=json.loads(manifest_path.read_text().split('=',1)[1].strip().rstrip(';'))
    except (ValueError,IndexError): pass

def encode(report):
    name=report.stem
    text=report.read_text()
    source=CAPTURE/name
    framefiles=sorted(source.glob('[0-9][0-9][0-9][0-9][0-9].jpg'))
    if len(framefiles)!=576: raise RuntimeError(f'{name}: expected 576 frames, got {len(framefiles)}')
    rate,channels,samples=re.search(r'sample rate=(\d+); channels=(\d+); samples=(\d+)',text).groups()
    args=['ffmpeg','-hide_banner','-loglevel','error','-y','-framerate','24','-i',str(source/'%05d.jpg')]
    has_audio=int(samples)>0
    if has_audio: args+=['-f','f32le','-ar',rate,'-ac',channels,'-i',str(source/'audio.f32')]
    # Simple label is burned into each clip so simulation provenance travels with the file.
    label='UNITY PLAY MODE  |  SIMULATED INPUT + ADAPTATION'
    font='/System/Library/Fonts/Helvetica.ttc'
    vf=f"drawtext=fontfile='{font}':text='{label}':fontsize=13:fontcolor=white:box=1:boxcolor=black@0.55:boxborderw=8:x=18:y=h-30"
    args+=['-vf',vf,'-c:v','libx264','-preset','fast','-crf','24','-pix_fmt','yuv420p','-movflags','+faststart','-t','24']
    if has_audio: args+=['-c:a','aac','-b:a','96k','-ac','2']
    args+=[str(OUTPUT/(name+'.mp4'))]
    subprocess.run(args,check=True)
    shutil.copy2(REPORT/(name+'.jpg'),OUTPUT/(name+'.jpg'))
    result=subprocess.check_output(['ffprobe','-v','error','-show_entries','format=duration','-of','csv=p=0',str(OUTPUT/(name+'.mp4'))],text=True)
    if abs(float(result.strip())-24)>.2: raise RuntimeError('Unexpected encoded duration: '+result)
    runtime_errors=int(re.search(r'Runtime errors=(\d+)',text).group(1))
    manifest[name]={'video':f'assets/recordings/{name}.mp4','poster':f'assets/recordings/{name}.jpg','simulated':True,'audio':has_audio,'seconds':24,'runtimeErrors':runtime_errors}
    if name=='spaceIsland': manifest[name]['reviewNote']='This Unity scene currently contains an empty sky and no island environment. Preview shows its actual current state.'
    elif runtime_errors: manifest[name]['reviewNote']=f'This recording completed with {runtime_errors} existing scene errors. Farm animal outline meshes require Read/Write access; review the scene before visitor use.'
    temporary=manifest_path.with_suffix('.tmp')
    temporary.write_text('window.KIOSK_RECORDINGS = '+json.dumps(manifest,indent=2)+';\n')
    temporary.replace(manifest_path)
    print('Encoded',name,'with audio' if has_audio else 'silent',flush=True)

watch='--watch' in sys.argv
while True:
    for report in sorted(REPORT.glob('*.txt')):
        if report.stem not in manifest: encode(report)
    status=(CAPTURE/'status.txt').read_text() if (CAPTURE/'status.txt').exists() else ''
    if not watch or status.startswith(('COMPLETE','FAILED','Cancelled')):
        print(status,flush=True);break
    time.sleep(3)
print(f'{len(manifest)} recordings available.',flush=True)
