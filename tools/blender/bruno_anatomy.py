"""Shared rest-space proportions for the compact IGR-757 bear revision."""
from mathutils import Vector


def remap(p):
    p=Vector(p)
    # Shorten the free limb, preserve broad paws and the depth of the torso.
    z=p.z if p.z<=.22 else (.22+(p.z-.22)*.68 if p.z<1.30 else p.z-.3456)
    # The head sits low ahead of the shoulder mantle; a short broad muzzle.
    head=max(0,min(1,(p.y-1.0)/.4))
    z-=.07*head
    y=p.y if p.y<=1.50 else 1.50+(p.y-1.50)*.84
    return Vector((p.x*1.10,y,z))


def unmap(p):
    p=Vector(p); y=p.y if p.y<=1.50 else 1.50+(p.y-1.50)/.84
    z=p.z+.07*max(0,min(1,(y-1.0)/.4))
    z=z if z<=.22 else (.22+(z-.22)/.68 if z<.9544 else z+.3456)
    return Vector((p.x/1.10,y,z))
