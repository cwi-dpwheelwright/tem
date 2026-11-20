export interface PackAction {
  id: string;
  label: string;
  actionType: string;
  args: Record<string, string | number | boolean | null>;
}

export interface PackModule {
  id: string;
  title?: string;
  surface?: string;
  actions: PackAction[];
}

export interface VerifiedPack {
  name: string;
  displayName?: string;
  version: string;
  hash: string;
  verifiedAt: string;
  modules: PackModule[];
  verificationScope: "root_and_dependencies";
  verifiedDependencies: string[];
}
