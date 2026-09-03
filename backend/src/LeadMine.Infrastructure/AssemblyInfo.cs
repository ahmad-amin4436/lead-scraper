using System.Runtime.CompilerServices;

// Lets LeadMine.Tests exercise a few test-only seams (SmtpProbe's port and
// retry delay) that must stay internal — nothing in production ever sets
// them, since a real SMTP probe always connects to port 25.
[assembly: InternalsVisibleTo("LeadMine.Tests")]
