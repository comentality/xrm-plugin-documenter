namespace PluginStepCodegen
{
    /// <summary>
    /// The settings behind the wizard hat: experiments that run as opt-ins until they earn
    /// being the default. Persisted through XrmToolBox's SettingsManager, which XML
    /// serialises - hence the public parameterless shape, and hence every member defaulting
    /// to off: a setting nobody saved yet must mean the tool as it always behaved.
    /// </summary>
    public class GuruSettings
    {
        /// <summary>
        /// Say a near-complete column list in the summary comment as its exceptions -
        /// "(all columns except ...)" - instead of reciting seventy names. Comment mode
        /// only; the attribute mode always writes the literal list it has to compile from.
        /// </summary>
        public bool AllColumnsExcept { get; set; }
    }
}
