import numpy as np, subprocess, os, json, glob
from PIL import Image
from scipy import ndimage as ndi
from hd_paths import W94 as W
RESR=W+'/tools/resr/realesrgan-ncnn-vulkan'

def load(stem,g=0,f=0):
    return np.array(Image.open(f'{W}/frames/{stem}__g{g}f{f}.png').convert('RGBA'))

def luma(rgb): return 0.299*rgb[...,0]+0.587*rgb[...,1]+0.114*rgb[...,2]

def nearest(rgba,s=4): return np.repeat(np.repeat(rgba,s,0),s,1)

def bleed(rgb,mask,iters=12):
    rgb=rgb.astype(np.float32).copy(); m=mask.copy()
    if not m.any(): return rgb
    mean=rgb[m].mean(0)
    k=np.ones((3,3),np.float32)
    for _ in range(iters):
        if m.all(): break
        wsum=ndi.convolve(m.astype(np.float32),k,mode='constant')
        new=(wsum>0)&~m
        if not new.any(): break
        acc=np.stack([ndi.convolve(rgb[...,c]*m,k,mode='constant') for c in range(3)],-1)
        rgb[new]=acc[new]/wsum[new][:,None]
        m=m|new
    rgb[~m]=mean
    return rgb

def interior_mask(a):
    """opaque pixels all of whose 8 neighbours are opaque; image border counts as opaque"""
    op=a>0
    pad=np.pad(op,1,constant_values=True)
    er=op.copy()
    for dy in (-1,0,1):
        for dx in (-1,0,1):
            er&=pad[1+dy:1+dy+op.shape[0],1+dx:1+dx+op.shape[1]]
    return er if er.any() else op

def resr_batch(items,tag):
    """items: dict name->float RGB (h,w,3) 0-255; returns dict name->(4h,4w,3) float"""
    ind=f'{W}/t/{tag}_in'; outd=f'{W}/t/{tag}_out'
    for d in (ind,outd):
        os.makedirs(d,exist_ok=True)
        for p in glob.glob(d+'/*'): os.remove(p)
    for n,rgb in items.items():
        Image.fromarray(np.clip(rgb+0.5,0,255).astype(np.uint8),'RGB').save(f'{ind}/{n}.png')
    r=subprocess.run([RESR,'-i',ind,'-o',outd,'-n','realesrgan-x4plus','-s','4','-f','png'],capture_output=True,cwd=W+'/tools/resr')
    out={}
    for n in items:
        out[n]=np.array(Image.open(f'{outd}/{n}.png').convert('RGB')).astype(np.float32)
    return out

def up_bicubic(x,s=4):
    h,w=x.shape[:2]
    if x.ndim==2:
        return np.array(Image.fromarray(x.astype(np.float32),'F').resize((w*s,h*s),Image.BICUBIC))
    return np.stack([up_bicubic(x[...,c],s) for c in range(x.shape[2])],-1)

def box_down(x,wts=None,s=4):
    h,w=x.shape[0]//s,x.shape[1]//s
    if wts is None:
        return x.reshape(h,s,w,s,*x.shape[2:]).mean((1,3))
    ws=wts.reshape(h,s,w,s).sum((1,3))
    if x.ndim==2: num=(x*wts).reshape(h,s,w,s).sum((1,3)); return num/np.maximum(ws,1e-6)
    num=(x*wts[...,None]).reshape(h,s,w,s,3).sum((1,3)); return num/np.maximum(ws,1e-6)[...,None]

def smooth_alpha(mask):
    b=nearest(mask.astype(np.float32)[...,None],4)[...,0]
    b=ndi.gaussian_filter(b,3.2)
    return np.clip((b-0.5)*3+0.5,0,1)

def bleed_err(err,mask,iters=12):
    # spread error into non-opaque area (single channel or multi)
    e3=err if err.ndim==3 else np.repeat(err[...,None],3,-1)
    e=bleed(e3,mask,iters)
    return e if err.ndim==3 else e[...,0]

def shading_lock(hd,src_rgb,mask,iters=4,color=False,sigma=1.0,coarse=0.0):
    """back-project luma (and optionally low-frequency chroma) error. hd float (4h,4w,3)"""
    hd=hd.copy(); m=mask
    wts=nearest(m.astype(np.float32)[...,None],4)[...,0]
    Ls=luma(src_rgb.astype(np.float32))
    for _ in range(iters):
        d=box_down(hd,wts)
        err=np.where(m,Ls-luma(d),0)
        err=bleed_err(err,m)
        if coarse>0: err=ndi.gaussian_filter(err,coarse)
        hd+=up_bicubic(err)[...,None]
    if color:
        for _ in range(2):
            d=box_down(hd,wts)
            Ld=luma(d)
            cs=src_rgb.astype(np.float32)-Ls[...,None]
            cd=d-Ld[...,None]
            err=np.where(m[...,None],cs-cd,0)
            err=bleed_err(err,m)
            err=ndi.gaussian_filter(err,(max(sigma,coarse),max(sigma,coarse),0))
            hd+=up_bicubic(err)
    return np.clip(hd,0,255)
