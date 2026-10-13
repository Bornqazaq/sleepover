"""Build a small deterministic hair-card colour/coverage atlas, 8 strand clusters."""
from pathlib import Path
import numpy as np
from PIL import Image

out=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover/art-source/BrunoMeshy/polish-v5')
width,height=1024,512
a=np.zeros((height,width,4),np.float32)
rng=np.random.default_rng(7575)
y=np.arange(height)[:,None]/(height-1)
# PNG top is the free end of the card, bottom is its embedded root.
t=1-y
for tile in range(8):
    x=np.arange(128)[None,:]/127
    cov=np.zeros((height,128))
    tone=np.zeros_like(cov)
    for j in range(11):
        root=(j+.5)/11+rng.uniform(-.018,.018)
        center=root+np.sin(t*rng.uniform(1.5,3.5)+rng.uniform(-1,1))*.017*t
        end=rng.uniform(.65,.99)
        radius=rng.uniform(.009,.017)*(1-.75*t)
        strand=np.clip((radius-np.abs(x-center))*240+0.5,0,1)
        strand*=np.clip((end-t)*65,0,1)
        weight=rng.uniform(.86,1.12)
        tone=np.where(strand>cov,weight,tone)
        cov=np.maximum(cov,strand)
    color=np.array([.97,.97,.97])[None,None,:]*(.88+.11*t[:,:,None])
    # Fine, aligned variation; never large random blotches.
    color=color*np.maximum(tone,.95)[:,:,None]
    a[:,tile*128:(tile+1)*128,:3]=color
    a[:,tile*128:(tile+1)*128,3]=cov
Image.fromarray(np.uint8(np.clip(a,0,1)*255),'RGBA').save(out/'Bruno_FurCards_RGBA.png')
print(out/'Bruno_FurCards_RGBA.png')
