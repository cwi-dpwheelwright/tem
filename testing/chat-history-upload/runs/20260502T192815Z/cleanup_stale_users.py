import json, os, urllib.request, urllib.parse

def post(url, payload, token=None):
    headers={'Content-Type':'application/json','Accept':'application/json'}
    if token: headers['Authorization']='Bearer '+token
    req=urllib.request.Request(url, data=json.dumps(payload).encode(), headers=headers, method='POST')
    with urllib.request.urlopen(req, timeout=60) as r:
        body=r.read().decode() or '{}'
        return json.loads(body) if body else {}

def get(url, token):
    req=urllib.request.Request(url, headers={'Accept':'application/json','Authorization':'Bearer '+token}, method='GET')
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode())
base='http://127.0.0.1:8085'
tenant=os.environ['TENANT']
token=post(base+'/auth/login', {'tenant_id':os.environ['IDENTITY_SUPERADMIN_TENANT_ID'], 'username':os.environ['IDENTITY_SUPERADMIN_USERNAME'], 'password':os.environ['IDENTITY_SUPERADMIN_PASSWORD']})['access_token']
count=0
for u in get(base+'/admin/users?'+urllib.parse.urlencode({'tenant_id':tenant}), token):
    username=(u.get('username') or '')
    if username.startswith('memory-smoke-a-results') or username.startswith('memory-smoke-b-results'):
        post(base+'/admin/users/'+u['user_id']+'/remove', {'tenant_id':tenant, 'reason':'cleanup after failed smoke setup'}, token)
        count += 1
print('cleaned_up_stale_users='+str(count))
