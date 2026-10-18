# Проверка раскладки секции: куски кода не должны налезать друг на друга
import sys, json
sys.path.insert(0,'/tmp')
mod=__import__(sys.argv[1] if len(sys.argv)>1 else 'pl_28')
blob,_,offs=mod.build(0x463000, 0x44DCD8, 0x44C8F8, 0x44c110, 0x44c114, 0x44c120, 0x44c13c, 0x44c140, 0x443E6F)
# известные куски: имя -> смещение
names=['sdl','medal','path','cb','restore','other','tex','apply','img','has','sync','decode','play','lvl','mem','phys','frame','beacon']
pts=sorted((offs[n], n) for n in names if n in offs)
bad=0
for (a,na),(b,nb) in zip(pts, pts[1:]):
    if a==b:
        print('НАЛОЖЕНИЕ: %s и %s на одном адресе %#x' % (na, nb, a)); bad+=1
print('кусков: %d, наложений: %d' % (len(pts), bad))
for a,n in pts: print('   %-9s %#06x' % (n, a))

# и закреплённые адреса: на них ссылаются перехваты, объявленные раньше
pinned={'sync': 0xE00, 'has': 0x1130}
bad2=0
for key, want in pinned.items():
    if key in offs and offs[key]!=want:
        print('ЗАКРЕПЛЁННЫЙ АДРЕС СЪЕХАЛ: %s на %#x вместо %#x' % (key, offs[key], want)); bad2+=1
print('закреплённых адресов сошлось: %d из %d' % (len(pinned)-bad2, len(pinned)))
