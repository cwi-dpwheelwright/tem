import XCTest
@testable import CwiIosCarplay

final class PackParserTests: XCTestCase {
    func testParseModulesFiltersAdminPage() {
        let rawJson = """
        {"ui":{"modules":[
            {"id":"1","type":"tab","title":"Home"},
            {"id":"2","type":"admin_page","title":"Admin"},
            {"id":"3","type":"tile","title":"Tile"}
        ]}}
        """
        let modules = PackParser.parseModules(from: rawJson)
        XCTAssertEqual(modules.count, 2)
        XCTAssertFalse(modules.contains { $0.type == "admin_page" })
    }

    func testParseModulesDecodesChildren() {
        let rawJson = """
        {"ui":{"modules":[
            {"id":"tab-1","type":"tab","title":"Main","children":[
                {"id":"card-1","type":"card","title":"Card","actions":[{"id":"a1","label":"Go","action_type":"do"}]}
            ]}
        ]}}
        """
        let modules = PackParser.parseModules(from: rawJson)
        XCTAssertEqual(modules.count, 1)
        let tab = modules.first
        XCTAssertEqual(tab?.children?.count, 1)
        XCTAssertEqual(tab?.children?.first?.type, "card")
    }

    func testParseModuleDataReference() {
        let rawJson = """
        {"ui":{"modules":[
            {"id":"card-1","type":"card","title":"Card","data":{"source":"api","path":"/api/status","method":"GET"}}
        ]}}
        """
        let modules = PackParser.parseModules(from: rawJson)
        let reference = modules.first?.dataReference
        XCTAssertEqual(reference?.source, "api")
        XCTAssertEqual(reference?.path, "/api/status")
        XCTAssertEqual(reference?.method, "GET")
    }

    func testParseModulesFromRootModules() {
        let rawJson = """
        {"modules":[
            {"id":"tile-1","type":"tile","title":"Tile"}
        ]}
        """
        let modules = PackParser.parseModules(from: rawJson)
        XCTAssertEqual(modules.count, 1)
        XCTAssertEqual(modules.first?.id, "tile-1")
    }
}

final class ModuleDataViewModelTests: XCTestCase {
    override func setUp() {
        super.setUp()
        URLProtocol.registerClass(URLProtocolStub.self)
    }

    override func tearDown() {
        URLProtocol.unregisterClass(URLProtocolStub.self)
        URLProtocolStub.handler = nil
        super.tearDown()
    }

    @MainActor
    func testFixtureLoad() async throws {
        let model = ModuleDataViewModel()
        let reference = DataReference(source: "fixture", path: nil, name: "module_data", method: nil)
        await model.load(
            reference: reference,
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            fixtureStore: FixtureStore(bundle: .main)
        )

        guard case .loaded(let value) = model.state,
              case .object(let object) = value else {
            XCTFail("Expected fixture to load")
            return
        }
        XCTAssertEqual(object["status"], .string("ok"))
    }

    @MainActor
    func testFixtureMissing() async {
        let model = ModuleDataViewModel()
        let reference = DataReference(source: "fixture", path: nil, name: "missing", method: nil)
        await model.load(
            reference: reference,
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            fixtureStore: FixtureStore(bundle: .main)
        )
        XCTAssertEqual(model.state, .error("Fixture not found"))
    }

    @MainActor
    func testApiPathMissing() async {
        let model = ModuleDataViewModel()
        let reference = DataReference(source: "api", path: nil, name: nil, method: nil)
        await model.load(
            reference: reference,
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            fixtureStore: FixtureStore(bundle: .main)
        )
        XCTAssertEqual(model.state, .error("Missing API path"))
    }

    @MainActor
    func testApiUnsupportedMethod() async {
        let model = ModuleDataViewModel()
        let reference = DataReference(source: "api", path: "/api/status", name: nil, method: "POST")
        await model.load(
            reference: reference,
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            fixtureStore: FixtureStore(bundle: .main)
        )
        XCTAssertEqual(model.state, .error("Unsupported method"))
    }

    @MainActor
    func testApiLoad() async throws {
        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/status")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            let data = Data("{\"ok\":true}".utf8)
            return URLProtocolStub.Stub(response: response!, data: data)
        }

        let model = ModuleDataViewModel()
        let reference = DataReference(source: "api", path: "/api/status", name: nil, method: "GET")
        await model.load(
            reference: reference,
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            fixtureStore: FixtureStore(bundle: .main),
            session: makeTestSession()
        )

        guard case .loaded(let value) = model.state,
              case .object(let object) = value else {
            XCTFail("Expected API data to load")
            return
        }
        XCTAssertEqual(object["ok"], .bool(true))
    }
}

private func makeTestSession() -> URLSession {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [URLProtocolStub.self]
    return URLSession(configuration: configuration)
}
