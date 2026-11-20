import Foundation
import CarPlay
import UIKit

final class CarPlayCoordinator {
    private let store = PackStore()
    private let killSwitchStore = KillSwitchStore()
    private let apiClient: APIClient
    private let tenantId: String
    private let userId: String
    private let policyMode: String
    private weak var interfaceController: CPInterfaceController?

    init(
        baseUrl: String,
        jwtToken: String,
        tenantId: String,
        userId: String,
        policyMode: String,
        interfaceController: CPInterfaceController?
    ) {
        self.apiClient = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        self.tenantId = tenantId
        self.userId = userId
        self.policyMode = policyMode
        self.interfaceController = interfaceController
    }

    private var actionRestrictionReason: String? {
        if let status = killSwitchStore.loadCached(), status.enabled {
            return "Action disabled: kill switch active."
        }
        let survivalMode = AppDefaults.survivalMode
        if survivalMode != "normal" {
            return "Action disabled: survival mode (\(survivalMode))."
        }
        if AppDefaults.offlineMode {
            return "Action disabled: offline mode active."
        }
        return nil
    }

    func buildTemplate() -> CPTemplate {
        let cached = store.loadVerifiedPack()
        let modules = cached.pack.map { PackParser.parseModules(from: $0.rawJson) } ?? []
        let carplayModules = modules.filter { $0.surface == "carplay" && $0.type == "tile" }
        if carplayModules.isEmpty {
            return CPListTemplate(title: "CWI", sections: [CPListSection(items: [
                CPListItem(text: "No CarPlay tiles available", detailText: cached.error)
            ])])
        }

        let buttons = carplayModules.map { module in
            let title = module.title ?? module.type
            let icon = UIImage(systemName: "car") ?? UIImage()
            return CPGridButton(titleVariants: [title], image: icon) { [weak self] _ in
                guard let self else { return }
                if let reason = self.actionRestrictionReason {
                    self.presentRestrictionAlert(reason)
                    return
                }
                guard let action = module.actions?.first else { return }
                Task {
                    _ = try? await self.apiClient.action(
                        tenantId: self.tenantId,
                        userId: self.userId,
                        policyMode: self.policyMode,
                        actionType: action.actionType,
                        args: action.args,
                        payload: action.payloadValue
                    )
                }
            }
        }
        let template = CPGridTemplate(title: "CWI", gridButtons: buttons)
        return template
    }

    private func presentRestrictionAlert(_ message: String) {
        guard let controller = interfaceController else { return }
        let action = CPAlertAction(title: "OK", style: .default) { _ in }
        let alert = CPAlertTemplate(titleVariants: ["Action Restricted", message], actions: [action])
        controller.presentTemplate(alert, animated: true, completion: nil)
    }
}
