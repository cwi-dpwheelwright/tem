import XCTest

final class ContentViewUITests: XCTestCase {
    func testTrustBannerUnknownByDefault() {
        let app = XCUIApplication()
        app.launchEnvironment = ["UI_TEST_MODE": "1"]
        app.launch()
        XCTAssertTrue(app.staticTexts["Pack trust: Unknown"].exists)
    }

    func testTrustBannerVerified() {
        let app = XCUIApplication()
        app.launchEnvironment = [
            "UI_TEST_MODE": "1",
            "UI_TEST_TRUST_STATE": "verified"
        ]
        app.launch()
        XCTAssertTrue(app.staticTexts["Pack trust: Verified"].exists)
    }

    func testTrustBannerUnverified() {
        let app = XCUIApplication()
        app.launchEnvironment = [
            "UI_TEST_MODE": "1",
            "UI_TEST_TRUST_STATE": "unverified"
        ]
        app.launch()
        XCTAssertTrue(app.staticTexts["Pack trust: Unverified"].exists)
    }

    func testOfflineBannerVisible() {
        let app = XCUIApplication()
        app.launchEnvironment = [
            "UI_TEST_MODE": "1",
            "UI_TEST_OFFLINE": "1"
        ]
        app.launch()
        XCTAssertTrue(app.staticTexts["Offline mode active."].exists)
    }

    func testActionDisabledWhenKillSwitchActive() {
        let app = XCUIApplication()
        app.launchEnvironment = [
            "UI_TEST_MODE": "1",
            "UI_TEST_KILL_SWITCH": "1"
        ]
        app.launch()
        XCTAssertTrue(app.buttons["Test Action"].exists)
        XCTAssertFalse(app.buttons["Test Action"].isEnabled)
    }

    func testApprovalStatusRendersWhenTicketPresent() {
        let app = XCUIApplication()
        app.launchEnvironment = [
            "UI_TEST_MODE": "1",
            "UI_TEST_APPROVAL_TICKET": "1",
            "UI_TEST_APPROVAL_STATUS": "approved"
        ]
        app.launch()
        XCTAssertTrue(app.staticTexts["Approval Ticket"].exists)
        XCTAssertTrue(app.staticTexts["status: approved"].exists)
    }

}
