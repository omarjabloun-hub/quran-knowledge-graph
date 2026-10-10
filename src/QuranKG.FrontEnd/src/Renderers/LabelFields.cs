namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// Composes <c>LabelField</c> values from the generated N constants. A LabelField is a list of
    /// FIELD NAMES ("A|B", the first with a value wins) or an interpolated template ("$…{Field}…");
    /// a name that does not exist renders as nothing and the label silently falls back to the raw
    /// UID. Built from N, a rename is a build error instead.
    ///
    /// <para>Keep these identical to the LabelField in
    /// src/QuranKG.Definitions/config/schemas/nodes/styles/*.json — the workspace resolves labels
    /// server-side (neighbour links, graph nodes) from the style it stores.</para>
    /// </summary>
    internal static class LabelFields
    {
        /// <summary>The first field with a value.</summary>
        public static string FirstOf(params string[] fields) => string.Join("|", fields);

        /// <summary>"$" + the template, its placeholders written as {Field}.</summary>
        public static string Template(string template) => "$" + template;

        public static string Field(string field) => "{" + field + "}";
    }
}
