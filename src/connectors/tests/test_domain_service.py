from cwi_connectors.execution import DomainServiceBase, ServiceResponse


class TestService(DomainServiceBase):
    def run(self):
        return self.execute(lambda: ServiceResponse.success({"ok": True}))


class ErrorService(DomainServiceBase):
    def run(self):
        def boom():
            raise ValueError("bad")
        return self.execute(boom)


def test_domain_service_success():
    result = TestService().run()
    assert result.ok
    assert result.status_code == 200


def test_domain_service_failure_maps_status():
    result = ErrorService().run()
    assert not result.ok
    assert result.status_code == 500
    assert result.error.code == "ValueError"
