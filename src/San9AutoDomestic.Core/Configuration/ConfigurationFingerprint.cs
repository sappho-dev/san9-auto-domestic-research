using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace San9AutoDomestic.Core.Configuration
{
    internal static class ConfigurationFingerprint
    {
        public static string Compute(DomesticConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            StringBuilder canonical = new StringBuilder();
            Append(canonical, configuration.SchemaVersion);
            Append(canonical, (int)configuration.CityScope);
            Append(canonical, (int)configuration.CityOrder);
            Append(canonical, configuration.ReserveMoney);
            Append(canonical, configuration.DryRunByDefault);
            Append(canonical, configuration.Profiles.Count);
            foreach (ProfileConfiguration profile in configuration.Profiles)
            {
                Append(canonical, profile.Id);
                Append(canonical, profile.DisplayName);
                Append(canonical, profile.Enabled);
                Append(canonical, profile.Tasks.Count);
                foreach (TaskConfiguration task in profile.Tasks)
                {
                    Append(canonical, (int)task.Command);
                    Append(canonical, task.Enabled);
                    Append(canonical, task.MinOfficers);
                    Append(canonical, task.MaxOfficers);
                    Append(canonical, task.RequireExactCount);
                    Append(canonical, (int)task.SelectionPolicy);
                    Append(canonical, task.ReserveMoneyOverride.HasValue);
                    if (task.ReserveMoneyOverride.HasValue)
                    {
                        Append(canonical, task.ReserveMoneyOverride.Value);
                    }
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        private static void Append(StringBuilder builder, string value)
        {
            string actual = value ?? string.Empty;
            builder.Append(actual.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(actual);
            builder.Append(';');
        }

        private static void Append(StringBuilder builder, int value)
        {
            Append(builder, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Append(StringBuilder builder, bool value)
        {
            Append(builder, value ? "1" : "0");
        }
    }
}
