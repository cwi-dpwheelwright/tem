import type { Metadata, Viewport } from "next";
import "./globals.css";
import { HistoryRetentionGate } from "@/components/history-retention";
import { StorageMigrationGate } from "@/components/storage-migration";
import { OfflineBanner } from "@/components/offline-banner";
import { AuthGate } from "@/components/auth-gate";
import { SessionBadge } from "@/components/session-badge";

export const metadata: Metadata = {
  title: "CWI GPT Web UI",
  description: "Pack-driven end-user client for governed autonomy workflows.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className="app-body">
        <StorageMigrationGate />
        <HistoryRetentionGate />
        <OfflineBanner />
        <AuthGate>
          <SessionBadge />
          {children}
        </AuthGate>
      </body>
    </html>
  );
}
