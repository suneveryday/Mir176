#!/usr/bin/env python3
"""Compile the audited catalog. Map binaries are supplied separately and verified."""
import argparse, hashlib, json, shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def merchant_services(npc):
    """Explicit engineering service policy, not recovered 2003 NPC scripts."""
    if npc['service'] != 'shop':
        return []
    result = ['buy'] if npc['goods'] else []
    result.append('sell')
    durable = {5, 6, 10, 11, 15, 19, 20, 21, 22, 23, 24, 26, 30}
    if durable.intersection(npc['item_modes']):
        result.append('repair')
        if any(word in npc['name'] for word in ('铁匠', '修理')):
            result.append('s_repair')
    return result

def write_legacy(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode('gb18030'))

def sql_value(value):
    if value is None: return 'NULL'
    if isinstance(value, int): return str(value)
    return "'" + value.replace('\\', '\\\\').replace("'", "''").replace('\0','\\0').replace('\n','\\n').replace('\r','\\r') + "'"

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--maps',type=Path,required=True);ap.add_argument('--output',type=Path,required=True);a=ap.parse_args()
    c=json.loads((ROOT/'content/catalog.json').read_text()); r=json.loads((ROOT/'content/level-experience.json').read_text())
    out=a.output
    if out.exists() and any(out.iterdir()): raise SystemExit('Output must be empty; never overwrite a running installation')
    game=out/'GameSrv';env=game/'Envir';assets={p.stem.casefold():p for p in a.maps.glob('*.map')}
    policy={'schema_version':1,'project':c['project'],'scope':c['scope'],
            'items':[{'id':x['Id'],'name':x['Name']} for x in c['items']],
            'skills':[{'id':x['MagID'],'name':x['MagName']} for x in c['skills']],
            'maps':[x['id'] for x in c['maps']],'monsters':[x['Name'] for x in c['monsters']], 'monster_display_names':c['monster_display_names']}
    game.mkdir(parents=True);(game/'classic-policy.json').write_text(json.dumps(policy,ensure_ascii=False,indent=2)+'\n')
    (game/'Map').mkdir()
    for row in c['maps']:
        p=assets[row['asset'].casefold()]
        if hashlib.sha256(p.read_bytes()).hexdigest()!=row['sha256']: raise SystemExit('Map asset hash mismatch: '+row['asset'])
        target=game/'Map'/(row['asset']+'.map')
        if not target.exists():shutil.copyfile(p,target)
    headers=[f"[{x['id']}"+(f"|{x['asset']}" if x['id']!=x['asset'] else '')+f" {x['title']}] {x['flags']}" for x in c['maps']]
    routes=[f"{x['from']} {x['x']},{x['y']} -> {x['to']} {x['tx']},{x['ty']}" for x in c['routes']]
    write_legacy(env/'MapInfo.txt','\n'.join(headers+['']+routes)+'\n')
    write_legacy(env/'MonGen.txt','\n'.join(f"{x['map']} {x['x']} {x['y']} {x['monster']} {x['radius']} {x['count']} {x['minutes']}" for x in c['spawns'])+'\n')
    for name,rows in c['drops'].items():
        write_legacy(env/'MonItems'/(name+'.txt'),'\n'.join(f"{x['numerator']}/{x['denominator']} {x['item']} {x['amount']}" for x in rows)+'\n')
    merchant_lines=[]
    for i,npc in enumerate(c['merchants'],1):
        name=f'classic-{i:03}';merchant_lines.append(f"{name} {npc['map']} {npc['x']} {npc['y']} {npc['name']} 0 {npc['appearance']} {npc['castle']}")
        if npc['service']=='storage':
            body='(@storage @getback @getnextpage @getPreviouspage)\n[@main]\n需要保管物品吗？\\\n<保管/@storage> <取回/@getback> <离开/@exit>\n[@storage]\n请放入要保管的物品。\\\n<返回/@main>\n[@getback]\n请选择要取回的物品。\\\n<返回/@main>\n'
        elif npc['service']=='weapon-upgrade':
            body='(@upgradenow @getbackupgnow)\n[@main]\n用黑铁矿石、首饰和金币升级手中武器。\\\n请先保管不打算使用的材料。\\\n<升级手中武器/@upgradenow> <取回武器/@getbackupgnow> <离开/@exit>\n[~@upgradenow_ok]\n已接收，请稍后回来领取。\\\n<返回/@main>\n[~@upgradenow_fail]\n无法开始升级，请检查武器、材料和金币。\\\n<返回/@main>\n[~@getbackupgnow_ok]\n已取回武器，结果将在首次使用时判定。\\\n<返回/@main>\n[~@getbackupgnow_ing]\n尚未完成。\\\n<返回/@main>\n[~@getbackupgnow_fail]\n没有可取回的武器。\\\n<返回/@main>\n'
        else:
            services=merchant_services(npc)
            labels={'buy':'购买','sell':'出售','repair':'修理','s_repair':'特殊修理'}
            body='('+' '.join('@'+x for x in services)+')\n%100\n'+''.join(f'+{m}\n' for m in npc['item_modes'])
            body+=f"[@main]\n这里是{npc['name']}，请选择服务。\\\n"
            body+=' '.join(f'<{labels[x]}/@{x}>' for x in services)+' <离开/@exit>\n'
            for label in services:
                body+=f'[@{label}]\n请选择物品。\\\n<返回/@main>\n'
            body+='[goods]\n'+''.join(f"{x['name']} {x['stock']} {x['minutes']}\n" for x in npc['goods'])
        write_legacy(env/'Market_Def'/(name+'-'+npc['map']+'.txt'),body)
    travel=[('1',52,32,'白日门向导',[('11',44,480,0)]),('11',44,480,'沃玛森林向导',[('1',52,32,0)]),('1',49,167,'封魔谷向导',[('4',449,187,0)]),('4',449,187,'沃玛森林向导',[('1',49,167,0)]),('0',329,266,'比奇老兵',[('3',330,330,2000)]),('3',327,327,'盟重老兵',[('0',330,266,2000),('5',141,337,5000)]),('5',138,329,'苍月老兵',[('3',330,330,5000)]),('E603',31,191,'老人',[('M001',102,102,10000)]),('M001',102,102,'向导',[('E603',31,191,0)])]
    for i,(map_id,x,y,name,targets) in enumerate(travel,1):
        script=f'classic-travel-{i:02}';merchant_lines.append(f'{script} {map_id} {x} {y} {name} 0 1 0')
        body='[@main]\n请选择目的地。\\\n'
        for j,(dest,tx,ty,fee) in enumerate(targets):
            title=next(row['title'] for row in c['maps'] if row['id']==dest);body+=f'<{title}（{fee}金币）/@go{j}>\\\n'
        body+='<离开/@exit>\n'
        for j,(dest,tx,ty,fee) in enumerate(targets):
            move='MAP M001' if dest=='M001' else f'MAPMOVE {dest} {tx} {ty}'
            body+=f'[@go{j}]\n#IF\nCHECKGOLD {fee}\n#ACT\nTAKE 金币 {fee}\n{move}\nBREAK\n#ELSESAY\n金币不足。\\\n<返回/@main>\n'
        write_legacy(env/'Market_Def'/(script+'-'+map_id+'.txt'),body)
    write_legacy(env/'GuardList.txt',''.join(f"{x['monster']} {x['map']} {x['x']}, {x['y']} : {x['direction']}\n" for x in c['guards']))
    write_legacy(env/'MiniMap.txt',''.join(f"{x['map']} {x['image']}\n" for x in c['minimaps']))
    write_legacy(env/'Merchant.txt','\n'.join(merchant_lines)+'\n')
    write_legacy(env/'Npcs.txt','比奇国王 1 0122 29 32 0 8\n沙巴克城堡管理 2 0150 7 16 0 8\n')
    write_legacy(env/'Npc_def/比奇国王-0122.txt','[@main]\n可以申请建立行会或行会战争。\\\n<创建行会/@@buildguildnow> <行会战争/@guildwar> <申请攻城/@requestcastlewar> <离开/@exit>\n[@guildwar]\n请输入对方行会。\\\n<申请战争/@@guildwar> <返回/@main>\n[@requestcastlewar]\n需祖玛头像和申请费用。\\\n<$REQUESTCASTLELIST> <返回/@main>\n')
    write_legacy(env/'Npc_def/沙巴克城堡管理-0150.txt','(@@sendmsg @@withdrawal @@receipts)\n[@main]\n占领行会：<$OWNERGUILD>\\\n城堡资金：<$CASTLEGOLD>\\\n<取款/@@withdrawal> <存款/@@receipts> <离开/@exit>\n')
    write_legacy(env/'StartPoint.txt','0 289 618\n0 650 631\n0 330 266\n2 503 483\n3 330 330\n4 240 200\n5 141 337\n11 178 326\n')
    for name in ('AdminList.txt','MakeItem.txt','MapQuest.txt','UnbindList.txt','MonSayMsg.txt','Notice/LineNotice.txt','MapQuest_def/QManage.txt','Market_Def/QFunction-0.txt'):
        write_legacy(env/name,'')
    for d in ('UserData','QuestDiary','Market_prices','Robot_def'):(env/d).mkdir(exist_ok=True)
    write_legacy(game/'Castle/List.txt','Sabuk\n');write_legacy(game/'Castle/Sabuk/SabukW.txt','[Setup]\nCastleName=沙巴克\n[Defense]\nCastleMap=3\nCastleMapList=3,0150,0151,0152,0153,0154,0155\nMainDoorName=沙巴克城门\nLeftWallName=沙巴克左城墙\nCenterWallName=沙巴克中城墙\nRightWallName=沙巴克右城墙\n')
    # Empty recruit slots have no actor until a castle owner hires defenders.
    castle=game/'Castle/Sabuk/SabukW.txt'
    with castle.open('ab') as target:
        target.write(''.join(f'{kind}_{slot}_HP=0\r\n' for kind in ('Archer','Guard') for slot in range(1,13)).encode('ascii'))
    xp='[Exp]\nKillMonExpMultiple=1\nUseFixExp=0\nMonDelHptoExp=0\nLimitExpLevel=48\nLimitExpValue=0\nLevel0=100\n'
    xp+=''.join(f"Level{level}={value}\n" for level,value in r['experience_to_next_level'].items())
    xp+='Level48=2147483647\n';write_legacy(game/'exps.conf',xp)
    sql=out/'mysql-init';sql.mkdir();ddl='SET NAMES utf8mb4;\nSET SESSION sql_mode=\'NO_AUTO_VALUE_ON_ZERO\';\n'
    for file in sorted((ROOT/'content/schema').glob('*.sql')):ddl+=file.read_text()+'\n'
    for table,key in [('stditems','items'),('magics','skills'),('monsters','monsters')]:
        ddl+='USE classic_data;\n'
        for row in c[key]:
            ddl+=f"INSERT INTO `{table}` ("+','.join('`'+k+'`' for k in row)+') VALUES ('+','.join(sql_value(v) for v in row.values())+');\n'
    (sql/'10-classic.sql').write_text(ddl)
    manifest={str(p.relative_to(out)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(out.rglob('*')) if p.is_file()}
    (out/'content-manifest.json').write_text(json.dumps({'schema_version':1,'catalog_sha256':hashlib.sha256((ROOT/'content/catalog.json').read_bytes()).hexdigest(),'files':manifest},ensure_ascii=False,indent=2)+'\n')
    print(json.dumps({'maps':len(c['maps']),'skills':len(c['skills']),'items':len(c['items']),'merchants':len(c['merchants']),'files':len(manifest)}))
if __name__=='__main__':main()
