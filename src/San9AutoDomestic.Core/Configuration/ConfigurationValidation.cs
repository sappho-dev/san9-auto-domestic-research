using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Core.Configuration
{
    public sealed class ConfigurationIssue
    {
        public ConfigurationIssue(string path, string message)
        {
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Path { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1}", Path, Message);
        }
    }

    public sealed class ConfigurationValidationException : Exception
    {
        private readonly ReadOnlyCollection<ConfigurationIssue> _issues;

        public ConfigurationValidationException(IEnumerable<ConfigurationIssue> issues)
            : base(BuildMessage(issues))
        {
            if (issues == null)
            {
                throw new ArgumentNullException("issues");
            }

            _issues = new ReadOnlyCollection<ConfigurationIssue>(new List<ConfigurationIssue>(issues));
        }

        public ReadOnlyCollection<ConfigurationIssue> Issues
        {
            get { return _issues; }
        }

        private static string BuildMessage(IEnumerable<ConfigurationIssue> issues)
        {
            if (issues == null)
            {
                return "Configuration validation failed.";
            }

            StringBuilder builder = new StringBuilder("Configuration validation failed:");
            foreach (ConfigurationIssue issue in issues)
            {
                builder.AppendLine();
                builder.Append(" - ");
                builder.Append(issue == null ? "Unknown configuration issue." : issue.ToString());
            }

            return builder.ToString();
        }
    }

    public static class ConfigurationValidator
    {
        public const int SupportedSchemaVersion = 1;

        public static void ValidateAndThrow(DomesticConfiguration configuration)
        {
            IList<ConfigurationIssue> issues = Validate(configuration);
            if (issues.Count != 0)
            {
                throw new ConfigurationValidationException(issues);
            }
        }

        public static IList<ConfigurationIssue> Validate(DomesticConfiguration configuration)
        {
            List<ConfigurationIssue> issues = new List<ConfigurationIssue>();
            if (configuration == null)
            {
                issues.Add(new ConfigurationIssue("$", "configuration is required"));
                return issues;
            }

            if (configuration.SchemaVersion <= 0)
            {
                issues.Add(new ConfigurationIssue("$.schemaVersion", "must be a positive integer"));
            }
            else if (configuration.SchemaVersion != SupportedSchemaVersion)
            {
                issues.Add(new ConfigurationIssue(
                    "$.schemaVersion",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "unsupported schema version {0}; supported version is {1}",
                        configuration.SchemaVersion,
                        SupportedSchemaVersion)));
            }

            if (configuration.Profiles.Count < 1 || configuration.Profiles.Count > 20)
            {
                issues.Add(new ConfigurationIssue("$.profiles", "must contain between 1 and 20 profiles"));
            }

            if (configuration.CityScope != CityScope.DirectCities)
            {
                issues.Add(new ConfigurationIssue("$.cityScope", "V1 only supports 'direct_cities'"));
            }

            if (configuration.CityOrder != CityOrder.GameIdAscending)
            {
                issues.Add(new ConfigurationIssue("$.cityOrder", "V1 only supports 'game_id_asc'"));
            }

            ValidateMoney(configuration.ReserveMoney, "$.reserveMoney", issues);

            HashSet<string> profileIds = new HashSet<string>(StringComparer.Ordinal);
            for (int profileIndex = 0; profileIndex < configuration.Profiles.Count; profileIndex++)
            {
                ProfileConfiguration profile = configuration.Profiles[profileIndex];
                string profilePath = string.Format(CultureInfo.InvariantCulture, "$.profiles[{0}]", profileIndex);
                if (profile == null)
                {
                    issues.Add(new ConfigurationIssue(profilePath, "profile must not be null"));
                    continue;
                }

                if (!IsAsciiIdentifier(profile.Id))
                {
                    issues.Add(new ConfigurationIssue(
                        profilePath + ".id",
                        "must be 1..64 ASCII identifier characters: letters, digits, '.', '_' or '-'"));
                }
                else if (!profileIds.Add(profile.Id))
                {
                    issues.Add(new ConfigurationIssue(profilePath + ".id", "duplicates an earlier profile id"));
                }

                if (!IsDisplayName(profile.DisplayName))
                {
                    issues.Add(new ConfigurationIssue(
                        profilePath + ".displayName",
                        "must contain 1..20 visible characters and no control characters"));
                }

                if (profile.Tasks.Count < 1 || profile.Tasks.Count > 20)
                {
                    issues.Add(new ConfigurationIssue(profilePath + ".tasks", "must contain between 1 and 20 tasks"));
                }

                for (int taskIndex = 0; taskIndex < profile.Tasks.Count; taskIndex++)
                {
                    TaskConfiguration task = profile.Tasks[taskIndex];
                    string taskPath = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}.tasks[{1}]",
                        profilePath,
                        taskIndex);
                    if (task == null)
                    {
                        issues.Add(new ConfigurationIssue(taskPath, "task must not be null"));
                        continue;
                    }

                    if (!Enum.IsDefined(typeof(DomesticCommand), task.Command) || task.Command == DomesticCommand.Unknown)
                    {
                        issues.Add(new ConfigurationIssue(taskPath + ".command", "is not a supported command"));
                    }

                    if (task.MinOfficers < 1
                        || task.MinOfficers > GameRules.MaximumDomesticOfficers)
                    {
                        issues.Add(new ConfigurationIssue(
                            taskPath + ".minOfficers",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "must be between 1 and {0}",
                                GameRules.MaximumDomesticOfficers)));
                    }

                    if (task.MaxOfficers < 1
                        || task.MaxOfficers > GameRules.MaximumDomesticOfficers)
                    {
                        issues.Add(new ConfigurationIssue(
                            taskPath + ".maxOfficers",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "must be between 1 and {0}",
                                GameRules.MaximumDomesticOfficers)));
                    }

                    if (task.MaxOfficers < task.MinOfficers)
                    {
                        issues.Add(new ConfigurationIssue(taskPath + ".maxOfficers", "must not be less than minOfficers"));
                    }

                    if (task.RequireExactCount && task.MinOfficers != task.MaxOfficers)
                    {
                        issues.Add(new ConfigurationIssue(
                            taskPath + ".requireExactCount",
                            "requires minOfficers and maxOfficers to be equal"));
                    }

                    if (!Enum.IsDefined(typeof(SelectionPolicy), task.SelectionPolicy)
                        || task.SelectionPolicy == SelectionPolicy.Unknown)
                    {
                        issues.Add(new ConfigurationIssue(
                            taskPath + ".selectionPolicy",
                            "must be 'native_best' or 'verified_stat_fallback'"));
                    }

                    if (task.ReserveMoneyOverride.HasValue)
                    {
                        ValidateMoney(
                            task.ReserveMoneyOverride.Value,
                            taskPath + ".reserveMoneyOverride",
                            issues);
                    }
                }
            }

            return issues;
        }

        private static void ValidateMoney(int money, string path, IList<ConfigurationIssue> issues)
        {
            if (money < 0 || money > GameRules.GameMoneyMax)
            {
                issues.Add(new ConfigurationIssue(
                    path,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "must be between 0 and {0}",
                        GameRules.GameMoneyMax)));
            }
        }

        private static bool IsAsciiIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool valid = (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '.'
                    || character == '_'
                    || character == '-';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDisplayName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 20 || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (char.IsControl(character)
                    || category == UnicodeCategory.Format
                    || category == UnicodeCategory.LineSeparator
                    || category == UnicodeCategory.ParagraphSeparator
                    || category == UnicodeCategory.Surrogate)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
