using System;
using System.Drawing;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace SsmsSqlFormatter.Options
{
    /// <summary>
    /// Reads and writes formatter settings as flat JSON (one property per key; colors as
    /// "#RRGGBB", enums as their name). Shared by the VSIX's Export/Import Formatter
    /// Settings commands and the CLI's --config option, so a file produced by one works
    /// with the other. Works against any settings object (reflection over its public
    /// properties), whether that's <see cref="GeneralOptions"/> or <see cref="FormatterSettings"/>.
    /// Every reflection pass here uses DeclaredOnly: <see cref="GeneralOptions"/> inherits
    /// from DialogPage, whose own base-class properties (e.g. AutomationObject) are COM/
    /// design-time objects that Newtonsoft.Json cannot serialize (self-referencing loop)
    /// and that must never appear in an exported settings file anyway.
    /// </summary>
    public static class FormatterSettingsSerializer
    {
        public static string ToJson(object settings)
        {
            var json = new JObject();
            foreach (var prop in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                object value;
                try { value = prop.GetValue(settings); } catch { continue; }

                if (value is Color color) json[prop.Name] = Hex(color);
                else if (value is Enum) json[prop.Name] = value.ToString();
                else json[prop.Name] = JToken.FromObject(value ?? "");
            }
            return json.ToString();
        }

        /// <summary>Applies matching properties from JSON onto an existing settings instance. Unknown or malformed values are skipped, never thrown.</summary>
        public static (int applied, int skipped) ApplyFromJson(object target, string json)
        {
            var obj = JObject.Parse(json);
            int applied = 0, skipped = 0;

            foreach (var prop in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                var token = obj[prop.Name];
                if (token == null) continue;

                try
                {
                    if (prop.PropertyType == typeof(Color))
                        prop.SetValue(target, ColorTranslator.FromHtml((string)token));
                    else if (prop.PropertyType.IsEnum)
                        prop.SetValue(target, Enum.Parse(prop.PropertyType, (string)token, true));
                    else
                        prop.SetValue(target, token.ToObject(prop.PropertyType));
                    applied++;
                }
                catch
                {
                    skipped++; // unknown or malformed value - keep the existing setting
                }
            }
            return (applied, skipped);
        }

        /// <summary>Creates a new <typeparamref name="T"/> and applies a JSON settings file to it - used by the CLI's --config option.</summary>
        public static T LoadFromJsonFile<T>(string path) where T : new()
        {
            var target = new T();
            ApplyFromJson(target, System.IO.File.ReadAllText(path));
            return target;
        }

        /// <summary>Shallow copy of a settings instance - same concrete type as the source - never mutates the source.</summary>
        public static IFormatterOptions Clone(IFormatterOptions source)
        {
            var type = source.GetType();
            var clone = Activator.CreateInstance(type);
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                try { prop.SetValue(clone, prop.GetValue(source)); } catch { /* skip */ }
            }
            return (IFormatterOptions)clone;
        }

        /// <summary>
        /// Clones the source and overlays a JSON settings file onto the clone - used to
        /// apply a folder-level .sqlformatter.json on top of the user's own Tools >
        /// Options settings for a single format operation, without ever touching (or
        /// persisting into) the user's actual settings.
        /// </summary>
        public static IFormatterOptions CloneAndApplyJson(IFormatterOptions source, string json)
        {
            var clone = Clone(source);
            ApplyFromJson(clone, json);
            return clone;
        }

        private static string Hex(Color c) =>
            "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }
}
