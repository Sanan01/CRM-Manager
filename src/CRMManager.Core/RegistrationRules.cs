using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CRMManager.Core
{
    public static class RegistrationRules
    {
        public static string NormalizeAttributes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var names = value.Split(',').Select(name => name.Trim().ToLowerInvariant()).ToArray();
            if (names.Any(name => !Regex.IsMatch(name, "^[a-z][a-z0-9_]*$")))
                throw new ArgumentException("Enter attribute logical names separated by commas, without empty entries.");
            return string.Join(",", names.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public static void ValidateStep(StepRegistration step)
        {
            if (step.PluginTypeId == Guid.Empty || step.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(step.Name))
                throw new ArgumentException("A plugin type, message, and step name are required.");
            if (step.Stage != 10 && step.Stage != 20 && step.Stage != 40)
                throw new ArgumentException("Select Pre-validation, Pre-operation, or Post-operation.");
            if ((step.Mode != 0 && step.Mode != 1) || (step.Mode == 1 && step.Stage != 40))
                throw new ArgumentException("Asynchronous steps are supported only in Post-operation.");
            if (step.Rank < 1)
                throw new ArgumentException("Execution order must be at least 1.");
            if (step.AsyncAutoDelete && step.Mode != 1)
                throw new ArgumentException("Automatic async job deletion is available only for asynchronous steps. Clear that option for a synchronous step.");
            if (NormalizeAttributes(step.FilteringAttributes).Length > 0
                && !string.Equals(step.MessageName, "Update", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Filtering attributes are supported here only for Update steps. Clear them for other messages.");
        }

        public static void ValidateImage(ImageRegistration image, StepRegistration step)
        {
            if (image.StepId == Guid.Empty || image.StepId != step.Id || string.IsNullOrWhiteSpace(image.Name)
                || string.IsNullOrWhiteSpace(image.Alias) || string.IsNullOrWhiteSpace(image.MessagePropertyName))
                throw new ArgumentException("An image requires its parent step, name, entity alias, and message property name.");
            if (image.ImageType < 0 || image.ImageType > 2)
                throw new ArgumentException("Select a pre-image, post-image, or both.");
            if (step.Stage != 20 && step.Stage != 40)
                throw new ArgumentException("Images are supported here only in Pre-operation and Post-operation.");
            var pre = image.ImageType == 0 || image.ImageType == 2;
            var post = image.ImageType == 1 || image.ImageType == 2;
            if (post && step.Stage != 40)
                throw new ArgumentException("Post-images require a Post-operation step.");
            if (pre && string.Equals(step.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Create has no pre-image.");
            if (post && string.Equals(step.MessageName, "Delete", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Delete has no post-image.");
            if (!image.AllAttributes && NormalizeAttributes(image.Attributes).Length == 0)
                throw new ArgumentException("Specify image attributes, or explicitly select All attributes.");
        }
    }
}
