import sys,json,collections
from hdlib import *
from skimage.color import rgb2lab
from skimage.measure import label

def hd(method,n):
    return np.array(Image.open(f'{W}/out/{method}/{n}.png').convert('RGBA'))

def holes_of(a):
    """interior transparent components (4-conn, not touching the border)"""
    t=a==0
    lab=label(t,connectivity=1)
    border=set(np.unique(np.concatenate([lab[0],lab[-1],lab[:,0],lab[:,-1]])))
    return [lab==i for i in range(1,lab.max()+1) if i not in border]

def iou(a,b):
    u=(a|b).sum(); return (a&b).sum()/u if u else 1.0

def gradmag(rgba):
    L=luma(rgba[...,:3].astype(np.float32))
    gy=ndi.sobel(L,0)/8; gx=ndi.sobel(L,1)/8
    return np.hypot(gx,gy)

def centroid(g,mask,box):
    x,y,w,h=box
    sub=g[y*4:(y+h)*4,x*4:(x+w)*4]*mask[y*4:(y+h)*4,x*4:(x+w)*4]
    s=sub.sum()
    if s<1e-3: return None
    ys,xs=np.mgrid[0:sub.shape[0],0:sub.shape[1]]
    return ((xs*sub).sum()/s/4+x,(ys*sub).sum()/s/4+y,s)

def frame_metrics(src,hdim,boxes):
    h,w=src.shape[:2]
    m0=src[...,3]>0
    a0=nearest(m0.astype(np.uint8)[...,None],4)[...,0]>0
    m1=hdim[...,3]>=128
    res={}
    res['alphaIoU']=iou(a0,m1)
    # colour key: majority vote per source px
    vote=m1.reshape(h,4,w,4).sum((1,3))>=8
    res['keyMismatchPx']=int((vote!=m0).sum())
    hl=holes_of(src[...,3])
    t1=~m1
    lab1=label(t1,connectivity=1)
    hi=[]
    for hmask in hl:
        A=nearest(hmask.astype(np.uint8)[...,None],4)[...,0]>0
        ids=np.bincount(lab1[A&t1]) if (A&t1).any() else np.array([0])
        ids[0]=0
        if ids.max()==0: hi.append(0.0); continue
        B=lab1==ids.argmax()
        hi.append(iou(A,B))
    res['holes']=len(hl)
    res['holeIoUmin']=min(hi) if hi else None
    # colour LF
    wts=hdim[...,3].astype(np.float32)/255
    dn=box_down(hdim[...,:3].astype(np.float32),wts)
    mk=m0
    if mk.any():
        lab_d=rgb2lab(np.clip(dn[mk],0,255)[None]/255)[0]
        lab_s=rgb2lab(src[...,:3][mk][None]/255.0)[0]
        de=np.sqrt(((lab_d-lab_s)**2).sum(1))
        res['dE_mean']=float(de.mean()); res['dE_p95']=float(np.percentile(de,95))
        res['lumaMAE']=float(np.abs(luma(dn)[mk]-luma(src[...,:3].astype(np.float32))[mk]).mean())
    # detail: deviation from own block mean
    Lh=luma(hdim[...,:3].astype(np.float32))
    bm=np.repeat(np.repeat(Lh.reshape(h,4,w,4).mean((1,3)),4,0),4,1)
    op=a0
    g0=gradmag(nearest(src)); g1=gradmag(hdim)
    if op.any(): res['sharp']=float(np.percentile(g1[op],99)/max(np.percentile(g0[op],99),1e-6))
    res['detail']=float(np.abs(Lh-bm)[op].mean()) if op.any() else 0.0
    return res

def repeated_parts(src,hdim,B=8,maxgroups=300,mindist=6):
    h,w=src.shape[:2]
    if h<B or w<B: return None
    from numpy.lib.stride_tricks import sliding_window_view
    rgb=src[...,:3].astype(np.uint32)
    key=(rgb[...,0]<<16)|(rgb[...,1]<<8)|rgb[...,2]
    op=src[...,3]>0
    win=sliding_window_view(key,(B,B))
    wop=sliding_window_view(op,(B,B)).all((2,3))
    rng=np.random.default_rng(1)
    wt=rng.integers(1,2**61,(B,B),dtype=np.uint64)
    hs=(win.astype(np.uint64)*wt).sum((2,3))
    nd=np.array([[len(set(win[y,x].ravel().tolist())) for x in range(0,1)] for y in range(0,1)])  # placeholder
    ys,xs=np.nonzero(wop)
    groups=collections.defaultdict(list)
    for y,x in zip(ys,xs): groups[int(hs[y,x])].append((y,x))
    sel=[]
    for k,v in groups.items():
        if len(v)<2: continue
        y,x=v[0]
        if len(set(win[y,x].ravel().tolist()))<6: continue
        # non-overlapping occurrences
        occ=[v[0]]
        for p in v[1:]:
            if all(abs(p[0]-q[0])>=B or abs(p[1]-q[1])>=B for q in occ): occ.append(p)
        if len(occ)>=2: sel.append(occ)
    sel.sort(key=lambda o:-len(o))
    sel=sel[:maxgroups]
    maes=[]
    for occ in sel:
        y0,x0=occ[0]
        ref=hdim[y0*4:(y0+B)*4,x0*4:(x0+B)*4,:3].astype(np.float32)
        for y,x in occ[1:]:
            blk=hdim[y*4:(y+B)*4,x*4:(x+B)*4,:3].astype(np.float32)
            maes.append(float(np.abs(blk-ref).mean()))
    return dict(groups=len(sel),pairs=len(maes),mae=float(np.mean(maes)) if maes else None)

def comp_luma(rgba,s=4):
    """luma composited over mid grey so that silhouette/hole edges count as visual elements"""
    a=rgba[...,3:4].astype(np.float32)/255
    L=luma(rgba[...,:3].astype(np.float32))
    return L*a[...,0]+128*(1-a[...,0])

def hotspot(src,hdim,boxes,blur_ui=1.0):
    """per rect: sub-pixel shift (UI px) of the method vs Legacy nearest, by phase correlation of the
    grey-composited luma blurred over blur_ui UI px; None when the Legacy rect is flat (std<4)"""
    from skimage.registration import phase_cross_correlation
    A=comp_luma(nearest(src)); B=comp_luma(hdim)
    out=[]
    for name,(x,y,w,h) in boxes:
        a=A[y*4:(y+h)*4,x*4:(x+w)*4]; b=B[y*4:(y+h)*4,x*4:(x+w)*4]
        if a.size==0 or a.std()<4: out.append((name,None)); continue
        a=ndi.gaussian_filter(a,blur_ui*4); b=ndi.gaussian_filter(b,blur_ui*4)
        win=np.outer(np.hanning(a.shape[0]),np.hanning(a.shape[1])) if min(a.shape)>8 else 1
        a=(a-a.mean())/(a.std()+1e-6); b=(b-b.mean())/(b.std()+1e-6)
        sh,_,_=phase_cross_correlation(a*win,b*win,upsample_factor=20,normalization=None)
        out.append((name,float(np.hypot(*sh)/4)))
    return out

def shift_of(a,b,up=20):
    """(dy,dx) in px: shift that registers b onto a, mean-removed normalised cross-correlation of windowed images"""
    from skimage.registration import phase_cross_correlation
    a=(a-a.mean())/(a.std()+1e-6); b=(b-b.mean())/(b.std()+1e-6)
    win=np.outer(np.hanning(a.shape[0]),np.hanning(a.shape[1]))
    sh,_,_=phase_cross_correlation(a*win,b*win,upsample_factor=up,normalization=None)
    return sh
