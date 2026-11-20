import type { Metadata } from "next";
import { Fraunces, Space_Grotesk } from "next/font/google";
import "./globals.css";
import { AuthProvider } from "@/lib/auth/context";
import { BreakGlassProvider } from "@/lib/break-glass/context";
import { TelemetryProvider } from "@/lib/telemetry/context";
import { TenantProvider } from "@/lib/tenant/context";

const spaceGrotesk = Space_Grotesk({
  variable: "--font-sans",
  subsets: ["latin"],
});

const fraunces = Fraunces({
  variable: "--font-display",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "CWI Glassbox Web UI",
  description: "Operator console for governed autonomy workflows.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className={`${spaceGrotesk.variable} ${fraunces.variable} antialiased`}>
        <AuthProvider>
          <TenantProvider>
            <BreakGlassProvider>
              <TelemetryProvider>{children}</TelemetryProvider>
            </BreakGlassProvider>
          </TenantProvider>
        </AuthProvider>
      </body>
    </html>
  );
}
