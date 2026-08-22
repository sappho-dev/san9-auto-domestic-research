using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace San9AutoDomestic.Core.Configuration
{
    public sealed class ConfigurationLoader
    {
        public DomesticConfiguration LoadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Configuration path is required.", "path");
            }

            string json = File.ReadAllText(path, new UTF8Encoding(false, true));
            return LoadJson(json);
        }

        public DomesticConfiguration LoadJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ConfigurationValidationException(new[]
                {
                    new ConfigurationIssue("$", "configuration JSON is required")
                });
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024;
            serializer.RecursionLimit = 64;

            object rootValue;
            try
            {
                rootValue = serializer.DeserializeObject(json);
            }
            catch (Exception exception)
            {
                throw new ConfigurationValidationException(new[]
                {
                    new ConfigurationIssue("$", "invalid JSON: " + exception.Message)
                });
            }

            StrictReader reader = new StrictReader();
            IDictionary<string, object> root = reader.AsObject(rootValue, "$", true);
            if (root == null)
            {
                throw new ConfigurationValidationException(reader.Issues);
            }

            reader.RejectUnknownFields(
                root,
                "$",
                new[] { "schemaVersion", "profiles", "cityScope", "cityOrder", "reserveMoney", "dryRunByDefault" });

            int schemaVersion = reader.RequiredInt(root, "schemaVersion", "$", 0);
            CityScope cityScope = ParseCityScope(reader.RequiredString(root, "cityScope", "$"), "$.cityScope", reader);
            CityOrder cityOrder = ParseCityOrder(reader.RequiredString(root, "cityOrder", "$"), "$.cityOrder", reader);
            int reserveMoney = reader.RequiredInt(root, "reserveMoney", "$", 0);
            bool dryRunByDefault = reader.RequiredBoolean(root, "dryRunByDefault", "$", false);

            IList<ProfileConfiguration> profiles = ReadProfiles(root, reader);
            DomesticConfiguration configuration = new DomesticConfiguration(
                schemaVersion,
                profiles,
                cityScope,
                cityOrder,
                reserveMoney,
                dryRunByDefault);

            IList<ConfigurationIssue> semanticIssues = ConfigurationValidator.Validate(configuration);
            foreach (ConfigurationIssue issue in semanticIssues)
            {
                reader.AddIssue(issue);
            }

            if (reader.Issues.Count != 0)
            {
                throw new ConfigurationValidationException(reader.Issues);
            }

            return configuration;
        }

        private static IList<ProfileConfiguration> ReadProfiles(
            IDictionary<string, object> root,
            StrictReader reader)
        {
            object[] profileValues = reader.RequiredArray(root, "profiles", "$", new object[0]);
            List<ProfileConfiguration> profiles = new List<ProfileConfiguration>();
            for (int profileIndex = 0; profileIndex < profileValues.Length; profileIndex++)
            {
                string path = string.Format(CultureInfo.InvariantCulture, "$.profiles[{0}]", profileIndex);
                IDictionary<string, object> profileValue = reader.AsObject(profileValues[profileIndex], path, true);
                if (profileValue == null)
                {
                    continue;
                }

                reader.RejectUnknownFields(profileValue, path, new[] { "id", "displayName", "enabled", "tasks" });
                string id = reader.RequiredString(profileValue, "id", path);
                string displayName = reader.RequiredString(profileValue, "displayName", path);
                bool enabled = reader.RequiredBoolean(profileValue, "enabled", path, false);
                object[] taskValues = reader.RequiredArray(profileValue, "tasks", path, new object[0]);
                List<TaskConfiguration> tasks = new List<TaskConfiguration>();

                for (int taskIndex = 0; taskIndex < taskValues.Length; taskIndex++)
                {
                    string taskPath = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}.tasks[{1}]",
                        path,
                        taskIndex);
                    IDictionary<string, object> taskValue = reader.AsObject(taskValues[taskIndex], taskPath, true);
                    if (taskValue == null)
                    {
                        continue;
                    }

                    reader.RejectUnknownFields(
                        taskValue,
                        taskPath,
                        new[]
                        {
                            "command",
                            "enabled",
                            "minOfficers",
                            "maxOfficers",
                            "requireExactCount",
                            "selectionPolicy",
                            "reserveMoneyOverride"
                        });

                    DomesticCommand command = ParseCommand(
                        reader.RequiredString(taskValue, "command", taskPath),
                        taskPath + ".command",
                        reader);
                    bool taskEnabled = reader.RequiredBoolean(taskValue, "enabled", taskPath, false);
                    int minOfficers = reader.RequiredInt(taskValue, "minOfficers", taskPath, 0);
                    int maxOfficers = reader.RequiredInt(taskValue, "maxOfficers", taskPath, 0);
                    bool requireExactCount = reader.RequiredBoolean(
                        taskValue,
                        "requireExactCount",
                        taskPath,
                        false);
                    SelectionPolicy policy = ParseSelectionPolicy(
                        reader.RequiredString(taskValue, "selectionPolicy", taskPath),
                        taskPath + ".selectionPolicy",
                        reader);
                    int? reserveOverride = reader.OptionalInt(taskValue, "reserveMoneyOverride", taskPath);

                    tasks.Add(new TaskConfiguration(
                        command,
                        taskEnabled,
                        minOfficers,
                        maxOfficers,
                        requireExactCount,
                        policy,
                        reserveOverride));
                }

                profiles.Add(new ProfileConfiguration(id, displayName, enabled, tasks));
            }

            return profiles;
        }

        private static CityScope ParseCityScope(string token, string path, StrictReader reader)
        {
            if (string.Equals(token, "direct_cities", StringComparison.Ordinal))
            {
                return CityScope.DirectCities;
            }

            reader.AddIssue(new ConfigurationIssue(path, "unknown city scope '" + token + "'"));
            return CityScope.Unknown;
        }

        private static CityOrder ParseCityOrder(string token, string path, StrictReader reader)
        {
            if (string.Equals(token, "game_id_asc", StringComparison.Ordinal))
            {
                return CityOrder.GameIdAscending;
            }

            reader.AddIssue(new ConfigurationIssue(path, "unknown city order '" + token + "'"));
            return CityOrder.Unknown;
        }

        private static DomesticCommand ParseCommand(string token, string path, StrictReader reader)
        {
            if (string.Equals(token, "patrol", StringComparison.Ordinal))
            {
                return DomesticCommand.Patrol;
            }

            if (string.Equals(token, "commerce", StringComparison.Ordinal))
            {
                return DomesticCommand.Commerce;
            }

            if (string.Equals(token, "cultivate", StringComparison.Ordinal))
            {
                return DomesticCommand.Cultivate;
            }

            if (string.Equals(token, "train", StringComparison.Ordinal))
            {
                return DomesticCommand.Train;
            }

            if (string.Equals(token, "repair", StringComparison.Ordinal))
            {
                return DomesticCommand.Repair;
            }

            reader.AddIssue(new ConfigurationIssue(path, "unknown command '" + token + "'"));
            return DomesticCommand.Unknown;
        }

        private static SelectionPolicy ParseSelectionPolicy(string token, string path, StrictReader reader)
        {
            if (string.Equals(token, "native_best", StringComparison.Ordinal))
            {
                return SelectionPolicy.NativeBest;
            }

            if (string.Equals(token, "verified_stat_fallback", StringComparison.Ordinal))
            {
                return SelectionPolicy.VerifiedStatFallback;
            }

            reader.AddIssue(new ConfigurationIssue(path, "unknown selection policy '" + token + "'"));
            return SelectionPolicy.Unknown;
        }

        private sealed class StrictReader
        {
            private readonly List<ConfigurationIssue> _issues = new List<ConfigurationIssue>();

            public IList<ConfigurationIssue> Issues
            {
                get { return _issues; }
            }

            public void AddIssue(ConfigurationIssue issue)
            {
                if (issue != null)
                {
                    _issues.Add(issue);
                }
            }

            public IDictionary<string, object> AsObject(object value, string path, bool required)
            {
                IDictionary<string, object> dictionary = value as IDictionary<string, object>;
                if (dictionary == null && required)
                {
                    _issues.Add(new ConfigurationIssue(path, "must be a JSON object"));
                }

                return dictionary;
            }

            public void RejectUnknownFields(
                IDictionary<string, object> value,
                string path,
                IEnumerable<string> allowedFields)
            {
                HashSet<string> allowed = new HashSet<string>(allowedFields, StringComparer.Ordinal);
                foreach (string field in value.Keys)
                {
                    if (!allowed.Contains(field))
                    {
                        _issues.Add(new ConfigurationIssue(path + "." + field, "unknown field"));
                    }
                }
            }

            public string RequiredString(IDictionary<string, object> value, string field, string path)
            {
                object raw;
                if (!value.TryGetValue(field, out raw))
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "is required"));
                    return string.Empty;
                }

                string result = raw as string;
                if (result == null)
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "must be a string"));
                    return string.Empty;
                }

                return result;
            }

            public bool RequiredBoolean(
                IDictionary<string, object> value,
                string field,
                string path,
                bool fallback)
            {
                object raw;
                if (!value.TryGetValue(field, out raw))
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "is required"));
                    return fallback;
                }

                if (!(raw is bool))
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "must be a boolean"));
                    return fallback;
                }

                return (bool)raw;
            }

            public int RequiredInt(
                IDictionary<string, object> value,
                string field,
                string path,
                int fallback)
            {
                object raw;
                if (!value.TryGetValue(field, out raw))
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "is required"));
                    return fallback;
                }

                int parsed;
                if (!TryReadInt(raw, out parsed))
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "must be an integer"));
                    return fallback;
                }

                return parsed;
            }

            public int? OptionalInt(IDictionary<string, object> value, string field, string path)
            {
                object raw;
                if (!value.TryGetValue(field, out raw))
                {
                    return null;
                }

                int parsed;
                if (!TryReadInt(raw, out parsed))
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "must be an integer when present"));
                    return null;
                }

                return parsed;
            }

            public object[] RequiredArray(
                IDictionary<string, object> value,
                string field,
                string path,
                object[] fallback)
            {
                object raw;
                if (!value.TryGetValue(field, out raw))
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "is required"));
                    return fallback;
                }

                object[] array = raw as object[];
                if (array == null)
                {
                    _issues.Add(new ConfigurationIssue(path + "." + field, "must be an array"));
                    return fallback;
                }

                return array;
            }

            private static bool TryReadInt(object value, out int result)
            {
                if (value is int)
                {
                    result = (int)value;
                    return true;
                }

                if (value is long)
                {
                    long longValue = (long)value;
                    if (longValue >= int.MinValue && longValue <= int.MaxValue)
                    {
                        result = (int)longValue;
                        return true;
                    }
                }

                result = 0;
                return false;
            }
        }
    }
}
