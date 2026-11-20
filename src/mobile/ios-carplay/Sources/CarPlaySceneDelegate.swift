import CarPlay

class CarPlaySceneDelegate: UIResponder, CPTemplateApplicationSceneDelegate {
    private var interfaceController: CPInterfaceController?

    func templateApplicationScene(_ templateApplicationScene: CPTemplateApplicationScene,
                                  didConnect interfaceController: CPInterfaceController) {
        self.interfaceController = interfaceController
        RuntimeConfigLoader.applyIfPresent()
        let token = KeychainStore().loadToken() ?? ""
        let coordinator = CarPlayCoordinator(
            baseUrl: AppDefaults.baseUrl,
            jwtToken: token,
            tenantId: AppDefaults.tenantId,
            userId: AppDefaults.userId,
            policyMode: AppDefaults.policyMode,
            interfaceController: interfaceController
        )
        if #available(iOS 14.0, *) {
            interfaceController.setRootTemplate(coordinator.buildTemplate(), animated: true, completion: nil)
        } else {
            interfaceController.setRootTemplate(coordinator.buildTemplate(), animated: true)
        }
    }
}

