namespace OutlookRulesVSTO.Core;

public static class RuleMigrator
{
    public static RuleConfiguration Migrate(RuleConfiguration configuration)
    {
        // Placeholder for future backward-compatible schema upgrades.
        configuration.SchemaVersion = Schema.CurrentSchemaVersion;
        foreach (var rule in configuration.Rules)
        {
            rule.SchemaVersion = Schema.CurrentSchemaVersion;
        }

        return configuration;
    }
}
