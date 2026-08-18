using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PluginStepCodegen.Logic
{
    /// <summary>
    /// Renders a plugin type's registration as Xrm Tools compatible attributes.
    ///
    /// Output follows the style of the XrmTools.Meta.Attributes README: the widest
    /// positional constructor the step's data supports, with the remaining facts as
    /// named properties.
    ///
    /// Ordering is load bearing. [Image] binds to the nearest preceding [Step], so
    /// steps and their images must be written in the order produced here.
    /// </summary>
    public static class AttributeEmitter
    {
        /// <summary>
        /// StepAttribute.State and StepAttribute.SupportedDeployment are declared as
        /// nullable enums upstream, which C# rejects as attribute named arguments
        /// (CS0655). They can never be emitted. Step state is deliberately not
        /// documented anyway: a registered plugin is a registered plugin.
        /// </summary>
        public static IEnumerable<string> Emit(PluginTypeInfo type)
        {
            var lines = new List<string> { EmitPlugin(type) };

            foreach (var step in type.Steps)
            {
                lines.Add(EmitStep(step, type.TypeName));
                lines.AddRange(step.Images.Select(EmitImage));
            }

            return lines;
        }

        /// <summary>
        /// The name Dataverse generates for a step when nobody supplies one. Emitting it
        /// back would be noise, so it is only written when it has been customised.
        /// </summary>
        private static string DefaultStepName(string typeName, string message, string entity, bool hasEntity)
        {
            return hasEntity
                ? typeName + ": " + message + " of " + entity
                : typeName + ": " + message;
        }

        private static string EmitPlugin(PluginTypeInfo type)
        {
            var named = new List<string>();
            if (!string.IsNullOrWhiteSpace(type.Description))
            {
                named.Add("Description = " + Literal(type.Description));
            }

            return named.Count == 0
                ? "[Plugin]"
                : "[Plugin(" + string.Join(", ", named) + ")]";
        }

        private static string EmitStep(PluginStepInfo step, string typeName)
        {
            var entity = step.PrimaryEntityName;
            // Global messages register against no entity; spkl writes "none", Xrm Tools
            // wants the constructor overload without an entity at all.
            var hasEntity = !string.IsNullOrWhiteSpace(entity)
                            && !string.Equals(entity, "none", StringComparison.OrdinalIgnoreCase);
            var hasFilter = !string.IsNullOrWhiteSpace(step.FilteringAttributes);

            var positional = new List<string> { Literal(step.MessageName) };
            if (hasEntity)
            {
                positional.Add(Literal(entity));
                if (hasFilter)
                {
                    positional.Add(Literal(step.FilteringAttributes));
                }
            }

            positional.Add(Stage(step.Stage));
            positional.Add(Mode(step.Mode));

            var named = new List<string>();
            if (!string.IsNullOrWhiteSpace(step.Name)
                && step.Name != DefaultStepName(typeName, step.MessageName, entity, hasEntity))
            {
                named.Add("Name = " + Literal(step.Name));
            }

            if (step.Rank != 1)
            {
                named.Add("ExecutionOrder = " + step.Rank.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(step.Description))
            {
                named.Add("Description = " + Literal(step.Description));
            }

            if (!string.IsNullOrWhiteSpace(step.Configuration))
            {
                named.Add("Configuration = " + Literal(step.Configuration));
            }

            if (step.AsyncAutoDelete)
            {
                named.Add("AsyncAutoDelete = true");
            }

            return Compose("Step", positional, named);
        }

        private static string EmitImage(PluginImageInfo image)
        {
            var positional = new List<string> { ImageType(image.ImageType) };
            if (!string.IsNullOrWhiteSpace(image.Attributes))
            {
                positional.Add(Literal(image.Attributes));
            }

            // Both Name and EntityAlias default to the image type's own name.
            var defaultName = TypeNameOf(image.ImageType);
            var named = new List<string>();
            if (!string.IsNullOrWhiteSpace(image.Name) && image.Name != defaultName)
            {
                named.Add("Name = " + Literal(image.Name));
            }

            if (!string.IsNullOrWhiteSpace(image.EntityAlias) && image.EntityAlias != defaultName)
            {
                named.Add("EntityAlias = " + Literal(image.EntityAlias));
            }

            if (!string.IsNullOrWhiteSpace(image.MessagePropertyName) && image.MessagePropertyName != "Target")
            {
                named.Add("MessagePropertyName = " + Literal(image.MessagePropertyName));
            }

            return Compose("Image", positional, named);
        }

        /// <summary>
        /// Single line when it stays readable, otherwise one named argument per line.
        /// </summary>
        private static string Compose(string name, List<string> positional, List<string> named)
        {
            var all = positional.Concat(named).ToList();
            var single = "[" + name + "(" + string.Join(", ", all) + ")]";
            if (named.Count == 0 || single.Length <= 120)
            {
                return single;
            }

            var sb = new StringBuilder();
            sb.Append("[").Append(name).Append("(").Append(string.Join(", ", positional));
            foreach (var argument in named)
            {
                sb.Append(",").Append(Environment.NewLine).Append("    ").Append(argument);
            }

            sb.Append(")]");
            return sb.ToString();
        }

        private static string Stage(int stage)
        {
            switch (stage)
            {
                case 10: return "Stages.PreValidation";
                case 20: return "Stages.PreOperation";
                case 30: return "Stages.MainOperation";
                case 40: return "Stages.PostOperation";
                // Stage 50 is retired and has no enum member in either the real package
                // or the generated one, so fall back to a cast that compiles against both.
                default: return "(Stages)" + stage.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string Mode(int mode)
        {
            return mode == 1 ? "ExecutionMode.Asynchronous" : "ExecutionMode.Synchronous";
        }

        private static string ImageType(int imageType)
        {
            return "ImageTypes." + TypeNameOf(imageType);
        }

        private static string TypeNameOf(int imageType)
        {
            switch (imageType)
            {
                case 0: return "PreImage";
                case 1: return "PostImage";
                default: return "Both";
            }
        }

        private static string Literal(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            var sb = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.Append("\"").ToString();
        }
    }
}
