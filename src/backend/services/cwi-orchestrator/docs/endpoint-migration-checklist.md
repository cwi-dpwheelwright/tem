# Endpoint Migration Checklist

Order of migration (suggested):
1) ASK
2) ACTION
3) APPROVAL
4) INGEST
5) ADMIN

Checklist
- [ ] Controller depends only on application services
- [ ] Domain service returns ServiceResponse<T>
- [ ] DataAccess accessed only via domain interfaces
- [ ] Error mapping uses stable codes
- [ ] Tests updated (unit + integration)
