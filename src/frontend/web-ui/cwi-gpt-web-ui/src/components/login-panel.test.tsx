import { cleanup, render, screen } from "@testing-library/react";
import { LoginPanel } from "@/components/login-panel";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: vi.fn(),
  }),
}));

describe("LoginPanel", () => {
  afterEach(() => {
    cleanup();
  });

  it("does not expose tenant input", () => {
    render(<LoginPanel />);
    expect(screen.queryByLabelText(/tenant/i)).toBeNull();
    expect(screen.queryByPlaceholderText(/tenant/i)).toBeNull();
    const inputs = Array.from(document.querySelectorAll("input"));
    const tenantInput = inputs.find((input) =>
      [input.id, input.name, input.placeholder].some((value) =>
        value?.toLowerCase().includes("tenant"),
      ),
    );
    expect(tenantInput).toBeUndefined();
  });

  it("offers registration from the login page", () => {
    render(<LoginPanel />);
    expect(screen.getByRole("button", { name: /register now/i })).toBeTruthy();
    expect(screen.getByLabelText(/username, email, or phone/i)).toBeTruthy();
  });
});
