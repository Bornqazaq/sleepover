"""Rasterize the three orthographic reference projections to a deterministic atlas.
Avoids Cycles' excessive texture footprint filtering on the sculpt's tiny charts.
"""
import bpy, numpy as np, math, struct, zlib

def bake(meshes,path,size=4096):
    image=next(i for i in bpy.data.images if i.name.startswith('Reference.png'))
    iw,ih=image.size;source=np.empty(iw*ih*4,dtype=np.float32);image.pixels.foreach_get(source);source=source.reshape(ih,iw,4)[:,:,:3]
    atlas=np.zeros((size,size,3),dtype=np.uint8);filled=np.zeros((size,size),dtype=bool)
    def tex(uv):
        x=np.clip(uv[:,0]*iw-.5,0,iw-1.001);y=np.clip(uv[:,1]*ih-.5,0,ih-1.001)
        ix=x.astype(int);iy=y.astype(int);fx=(x-ix)[:,None];fy=(y-iy)[:,None]
        return (source[iy,ix]*(1-fx)+source[iy,ix+1]*fx)*(1-fy)+(source[iy+1,ix]*(1-fx)+source[iy+1,ix+1]*fx)*fy
    def srgb(c):
        c=np.array(c);return np.where(c<.0031308,c*12.92,1.055*np.maximum(0,c)**(1/2.4)-.055)
    for ob in meshes:
        me=ob.data;me.calc_loop_triangles();n=len(me.loops)
        uv=np.empty(n*2,dtype=np.float32);me.uv_layers['GameAtlas'].data.foreach_get('uv',uv);uv=uv.reshape(n,2)*size
        projections=[]
        for name in ['FrontProjection','BackProjection','SideProjection']:
            layer=me.uv_layers.get(name)
            if layer:
                a=np.empty(n*2,dtype=np.float32);layer.data.foreach_get('uv',a);projections.append(a.reshape(n,2))
        weights=None
        if me.color_attributes.get('ProjectionWeights'):
            a=np.empty(len(me.vertices)*4,dtype=np.float32);me.color_attributes['ProjectionWeights'].data.foreach_get('color',a);weights=a.reshape(-1,4)[:,:3]
        mats=[]
        for mat in me.materials:
            if mat.name.startswith('Ringmaster • reference paint'):mats.append(None)
            elif mat.name.startswith('Chestnut mane'):mats.append(srgb((.065,.028,.014)))
            else:
                p=next((x for x in mat.node_tree.nodes if x.type=='BSDF_PRINCIPLED'),None)
                mats.append(srgb(p.inputs['Base Color'].default_value[:3] if p else (.03,.03,.03)))
        for tri in me.loop_triangles:
            ids=list(tri.loops);v=uv[ids];a,b,c=v
            det=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(det)<1e-6:continue
            lo=np.maximum(0,np.floor(v.min(axis=0)).astype(int));hi=np.minimum(size-1,np.ceil(v.max(axis=0)).astype(int))
            xx,yy=np.meshgrid(np.arange(lo[0],hi[0]+1),np.arange(lo[1],hi[1]+1));x=xx.ravel()+.5;y=yy.ravel()+.5
            wa=((b[1]-c[1])*(x-c[0])+(c[0]-b[0])*(y-c[1]))/det
            wb=((c[1]-a[1])*(x-c[0])+(a[0]-c[0])*(y-c[1]))/det;wc=1-wa-wb
            keep=(wa>=-.0001)&(wb>=-.0001)&(wc>=-.0001)
            if not keep.any():continue
            bary=np.column_stack((wa[keep],wb[keep],wc[keep]));px=xx.ravel()[keep];py=yy.ravel()[keep]
            col=mats[tri.material_index]
            if col is None:
                w=bary@weights[list(tri.vertices)];rgb=np.zeros((len(bary),3))
                for j,proj in enumerate(projections):rgb+=tex(bary@proj[ids])*w[:,j,None]
            else:rgb=np.broadcast_to(col,(len(bary),3))
            atlas[py,px]=np.clip(rgb*255+.5,0,255).astype(np.uint8);filled[py,px]=True
    for iteration in range(10):
        old=filled.copy()
        for dy,dx in [(1,0),(-1,0),(0,1),(0,-1)]:
            valid=np.roll(old,(dy,dx),(0,1))&~filled
            if dy==1:valid[0]=False
            if dy==-1:valid[-1]=False
            if dx==1:valid[:,0]=False
            if dx==-1:valid[:,-1]=False
            atlas[valid]=np.roll(atlas,(dy,dx),(0,1))[valid];filled[valid]=True
    def chunk(tag,data):return struct.pack('!I',len(data))+tag+data+struct.pack('!I',zlib.crc32(tag+data)&0xffffffff)
    raw=b''.join(b'\0'+row.tobytes() for row in atlas[::-1])
    png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',size,size,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw,6))+chunk(b'IEND',b'')
    path.write_bytes(png)
    result=bpy.data.images.load(str(path),check_existing=False);result.name='HorseRingmaster_Albedo';result.pack()
    print('Reference atlas',size,'coverage',float(filled.mean()));return result
