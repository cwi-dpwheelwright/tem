import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { loadUseWeb, saveUseWeb } from "./storage";

describe("storage", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  afterEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it("defaults trusted web retrieval to off", () => {
    expect(loadUseWeb()).toBe(false);
  });

  it("persists trusted web retrieval preference", () => {
    saveUseWeb(true);
    expect(loadUseWeb()).toBe(true);

    saveUseWeb(false);
    expect(loadUseWeb()).toBe(false);
  });
});
