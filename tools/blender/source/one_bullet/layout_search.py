import random,math,json
random.seed(731); N=9; nodes=list(range(N*N)); allE=[(a,a+d) for a in nodes for d in (1,N) if (d==1 and a%N<N-1) or (d==N and a//N<N-1)]
def adjacency(es):
 adj=[set() for _ in nodes]
 for a,b in es: adj[a].add(b);adj[b].add(a)
 return adj
def connected(adj):
 seen={0};stack=[0]
 while stack:
  a=stack.pop()
  for b in adj[a]-seen:seen.add(b);stack.append(b)
 return len(seen)==len(nodes)
def score(es):
 adj=adjacency(es)
 if not connected(adj):return 10000
 counts=[sum(len(s)==i for s in adj) for i in range(5)]
 straight=sum(len(s)==2 and sum(s)==2*a for a,s in enumerate(adj))
 # Final graph: 10 dead ends, 14 T, 4 crosses, 7 independent cycles.
 return sum(abs(counts[k]-v)*8 for k,v in [(1,10),(3,14),(4,4)])+straight*4
# Random DFS then seven extra passages.
es=set();seen={0};stack=[0]
while stack:
 a=stack[-1]; opts=[b for e in allE if a in e for b in e if b!=a and b not in seen]
 if opts:
  b=random.choice(opts);es.add(tuple(sorted((a,b))));seen.add(b);stack.append(b)
 else:stack.pop()
for e in random.sample([e for e in allE if e not in es],7):es.add(e)
cur=score(es);best=(cur,es.copy())
for i in range(250000):
 rem=random.choice(tuple(es)); add=random.choice(allE)
 if add in es:continue
 new=es-{rem}|{add};v=score(new);temp=max(.12,3.5*(1-(i%50000)/50000))
 if v<cur or random.random()<math.exp(min(0,(cur-v)/temp)):es=new;cur=v
 if cur<best[0]:best=(cur,es.copy())
 if cur==0:break
es=best[1];adj=adjacency(es)
print('score',best[0],'iterations',i,'degrees',{k:sum(len(s)==k for s in adj) for k in (1,2,3,4)},'straight',sum(len(s)==2 and sum(s)==2*a for a,s in enumerate(adj)))
# Hand author two small widened rooms at junctions, choose opposing quadrants.
rooms=[min((a for a in nodes if len(adj[a])>=3),key=lambda a:(a%N-2)**2+(a//N-2)**2),min((a for a in nodes if len(adj[a])>=3),key=lambda a:(a%N-6)**2+(a//N-6)**2)]
def dist(a,b):return (a%N-b%N)**2+(a//N-b//N)**2
spawn=[0,80,8,72]
while len(spawn)<8:spawn.append(max((a for a in nodes if a not in spawn and a not in rooms),key=lambda a:min(dist(a,b) for b in spawn)))
gun=[a for a in nodes if len(adj[a])==1]
while len(gun)<12:gun.append(max((a for a in nodes if a not in gun and a not in rooms),key=lambda a:min(dist(a,b) for b in gun)))
obj={'grid':N,'pitch':4.8,'width':2.16,'edges':[{'a':a,'b':b} for a,b in sorted(es)],'rooms':rooms,'spawns':spawn,'guns':gun}
open('/tmp/onebullet-layout.json','w').write(json.dumps(obj,indent=2))
