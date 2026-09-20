"""Deterministic original synthesized quarantine ambience and paint feedback."""
from pathlib import Path
import math,random,wave,array
root=Path(__file__).resolve().parents[2]
out=root/'igruha/Assets/_Project/Audio/Infection';out.mkdir(parents=True,exist_ok=True)
rate=24000
for name,duration in [('Wind',12),('Fire',8),('Creak',6.4),('Splat',.6),('WetStep',.19),('Zero',1),('Heartbeat',1),('Whistle',.85)]:
 rng=random.Random(211);samples=[];low=0
 for i in range(int(rate*duration)):
  t=i/rate;noise=rng.uniform(-1,1);low=.985*low+.015*noise
  if name=='Wind':v=low*(.6+.25*math.sin(t*math.tau/duration))+.018*math.sin(t*math.tau*83)*(math.sin(t*math.tau/duration)**2)
  elif name=='Fire':v=low*.6+noise*.018+(noise*.35 if rng.random()<.0014 else 0)
  elif name=='Creak':
   phase=t%3.2;env=max(0,math.sin(phase*math.tau/3.2))**8
   v=env*(.08*math.sin(math.tau*(410*t+28*math.sin(t*2)))+.026*noise)
  elif name=='Splat':v=(noise*.3+math.sin(math.tau*(180*t-90*t*t))*.35)*math.exp(-t*9)*(1-math.exp(-t*100))
  elif name=='WetStep':v=(low*1.1+noise*.045+math.sin(math.tau*(130*t-190*t*t))*.18)*math.exp(-t*23)*(1-math.exp(-t*150))
  elif name=='Zero':v=.10*(math.sin(math.tau*240*t)+.4*math.sin(math.tau*345*t))*math.sin(math.pi*t/duration)**2
  elif name=='Heartbeat':v=sum(.22*math.sin(math.tau*58*(t-a))*math.exp(-max(0,t-a)*30) if t>a else 0 for a in(.03,.22))
  else:v=.15*(math.sin(math.tau*1700*t)+.3*math.sin(math.tau*1950*t))*math.sin(math.pi*t/duration)**2
  fade=min(1,t/.02,(duration-t)/.02);samples.append(max(-1,min(1,v*fade)))
 peak=max(abs(v) for v in samples);gain=.75/peak
 data=array.array('h',(int(v*gain*32767) for v in samples))
 with wave.open(str(out/(name+'.wav')),'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(rate);w.writeframes(data.tobytes())
 print(name,len(samples),f'peak {peak*gain:.2f}')
