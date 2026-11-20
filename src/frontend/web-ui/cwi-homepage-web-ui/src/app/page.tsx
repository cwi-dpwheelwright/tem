const navItems = ["Home", "Architecture", "Trust Model", "Transparency"];
const trustStages = ["Ask", "Action", "ApprovalTicket", "Ledger"];

const betaUrl = process.env.NEXT_PUBLIC_CWI_BETA_ACCESS_URL ?? "#";

export default function HomePage() {
  return (
    <main className="homepage">
      <div className="stars" />
      <div className="mesh-overlay" />
      <header className="site-header">
        <div className="logo-mark">
          <span className="logo-shield" />
          <div>
            <p className="logo-word">CWI</p>
            <p className="logo-tagline">Protect. Survive. Prevail.</p>
          </div>
        </div>
        <nav className="top-nav">
          {navItems.map((item) => (
            <a key={item} href={`#${item.toLowerCase().replace(/\s+/g, "-")}`}>
              {item}
            </a>
          ))}
        </nav>
        <a className="pill-button" href={betaUrl}>
          Beta Access
        </a>
      </header>

      <section className="hero" id="home" aria-label="Hero">
        <h1>
          The Mesh That <strong>Thinks.</strong>
          <br />
          The Intelligence <strong>That Survives.</strong>
        </h1>
        <p className="hero-copy">
          CWI transforms open-weight AI into a governed, resilient, tamper-evident multi-band intelligence
          system.
        </p>
        <div className="hero-ctas">
          <a className="outline-button" href="#architecture">
            Explore the Architecture
          </a>
          <a className="outline-button" href="#trust-model">
            View the Trust Model
          </a>
        </div>
      </section>

      <section className="architecture" id="architecture">
        <div className="architecture-copy">
          <h2>
            <span>Inference-Aware</span> Mesh Orchestration
          </h2>
          <p>
            Task-aware routing across HaLoW, LoRa, CBRS, and remote inference while respecting policy and
            survival rules.
          </p>
          <a className="solid-button" href={betaUrl}>
            Learn More
          </a>
        </div>
        <div className="architecture-diagram" aria-hidden="true">
          <div className="orbit orbit-a" />
          <div className="orbit orbit-b" />
          <div className="node node-a" />
          <div className="node node-b" />
          <div className="node node-c" />
          <div className="chip">Governance Edges</div>
        </div>
      </section>

      <section className="dual-panel" id="trust-model">
        <article className="trust-card">
          <h3>
            <span>Policy-Governed</span> by Design
          </h3>
          <p>
            CWI turns gated policy into hardened inference across HaLoW, LoRa, CBRS, and remote lanes.
          </p>
          <div className="trust-timeline">
            {trustStages.map((stage) => (
              <span key={stage}>{stage}</span>
            ))}
          </div>
          <div className="trust-note">Resolvable outputs with operator reason and immutable lineage.</div>
          <a className="outline-button compact" href={betaUrl}>
            View the Trust
          </a>
        </article>

        <article className="domain-card">
          <h3>
            <span>Multi-Band.</span> Multi-Model. Multi-Domain.
          </h3>
          <div className="domain-visual" aria-hidden="true">
            <div className="domain-core" />
            <div className="domain-ring r1" />
            <div className="domain-ring r2" />
            <div className="domain-ring r3" />
          </div>
          <a className="solid-button compact" href={betaUrl}>
            Read the Paper
          </a>
        </article>
      </section>

      <section className="transparency-banner" id="transparency">
        <div className="logo-mark compact">
          <span className="logo-shield" />
          <p className="logo-word">CWI</p>
        </div>
        <div>
          <h3>Glassbox Transparency</h3>
          <p>Every answer includes lineage, policy state, and execution provenance.</p>
        </div>
        <a className="pill-button" href={betaUrl}>
          Get Beta Access
        </a>
      </section>
    </main>
  );
}
