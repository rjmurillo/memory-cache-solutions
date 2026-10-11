---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.sln"
---

## When reviewing C# code (\*.cs)

I need your help tracking down and fixing some bugs that have been reported in this codebase.

I suspect the bugs are related to:

- Incorrect handling of edge cases
- Off-by-one errors in loops or array indexing
- Unexpected data types
- Uncaught exceptions
- Concurrency issues
- Improper configuration settings

To diagnose:

1. Review the code carefully and systematically
2. Trace the relevant code paths
3. Consider boundary conditions and potential error states
4. Look for antipatterns that tend to cause bugs
5. Run the code mentally with example inputs
6. Think about interactions between components

When you find potential bugs, for each one provide:

1. File path and line number(s)
2. Description of the issue and why it's a bug
3. Example input that would trigger the bug
4. Suggestions for how to fix it

After analysis, please update the code with your proposed fixes. Try to match the existing code style. Add regression tests if possible to prevent the bugs from recurring.

I appreciate your diligence and attention to detail! Let me know if you need any clarification on the intended behavior of the code.

---

## XML Documentation Comments Guidance for C#

When writing XML documentation comments for C# code, follow these best practices to ensure clarity, consistency, and compatibility with documentation tools:

### General Principles

- **Document ALL public, protected, and internal types and members** with XML comments. This is MANDATORY for this codebase.
- **Private members may be documented if useful** for understanding complex logic or algorithms.
- **Always use complete sentences** ending with periods.
- **XML must be well-formed**; invalid XML will cause compiler warnings.
- **At a minimum, every type and member should have a `<summary>` tag.**
- **Internal types and members are treated with the same documentation requirements as public APIs** since they are part of the library's internal contract and are used by AI agents and developers for understanding the codebase.

### Tag Usage and Structure

- Use `<summary>` to briefly describe the purpose of a type or member.
- Use `<remarks>` for additional or supplemental information.
- Use `<param name="paramName">` to describe each method parameter. The name must match the method signature.
- Use `<returns>` to describe the return value of a method.
- Use `<exception cref="ExceptionType">` to document exceptions that may be thrown.
- Use `<value>` to describe the value of a property.
- Use `<typeparam name="T">` and `<typeparamref name="T"/>` for generic type parameters.
- Use `<example>` to provide usage examples, often with `<code>` blocks.
- Use `<para>`, `<list>`, `<c>`, `<code>`, `<b>`, `<i>`, `<u>`, `<br/>`, and `<a>` for formatting and structure as needed.
- Use `<inheritdoc/>` to inherit documentation from base members or interfaces when appropriate.

### Referencing Types, Members, and Keywords

- **Whenever referencing a type, member, or exception in documentation, you MUST use `<see cref="..."/>` or `<seealso cref="..."/>`.**
  - Example: `See <see cref="System.String"/> for details.`
  - Example: `Throws <see cref="ArgumentNullException"/> if <paramref name="foo"/> is null.`
- **When referencing C# language keywords (such as `true`, `false`, `null`, `async`, etc.), use `<see langword="keyword"/>`.**
  - Example: `Returns <see langword="true"/> if the operation succeeds; otherwise, <see langword="false"/>.`
- **Do NOT use plain text for code elements or keywords.** Always use the appropriate XML tag for references.
- For inline code or identifiers that are not references, use `<c>code</c>`.

### Additional Best Practices

- Use `<paramref name="paramName"/>` to refer to parameters in documentation text.
- Use `<typeparamref name="T"/>` to refer to generic type parameters in text.
- For external URLs, use `<see href="https://example.com">Link Text</see>` or `<seealso href="https://example.com">Link Text</seealso>`.
- If you need to show angle brackets in text, use `&lt;` and `&gt;`.
- Avoid duplicating documentation by using `<inheritdoc/>` or `<include/>` where possible.

### Example

```csharp
/// <summary>
/// Attempts to parse the specified <paramref name="input"/> as an integer.
/// Returns <see langword="true"/> if parsing succeeds; otherwise, <see langword="false"/>.
/// </summary>
/// <param name="input">The string to parse.</param>
/// <param name="result">When this method returns, contains the parsed value if successful; otherwise, <see langword="null"/>.</param>
/// <returns><see langword="true"/> if parsing was successful; otherwise, <see langword="false"/>.</returns>
/// <exception cref="ArgumentNullException">Thrown if <paramref name="input"/> is <see langword="null"/>.</exception>
```

For more details, see the [Microsoft Learn XML documentation tags guide](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags).

### Mandatory Documentation Requirements

**CRITICAL: All public, protected, and internal types and members MUST be documented with XML comments.**

This requirement applies to:

- **All public types and members** (classes, interfaces, structs, enums, methods, properties, fields, events)
- **All protected types and members** (inheritance-visible APIs)
- **All internal types and members** (assembly-visible APIs)

**Rationale:**

- Internal APIs are part of the library's contract and are used by AI agents and developers
- Comprehensive documentation improves code maintainability and understanding
- AI agents rely on XML documentation for code analysis and generation
- Internal types often represent important architectural decisions that need explanation

**Enforcement:**

- The build system treats missing XML documentation as errors (CS1591)
- All commits must include proper XML documentation for new or modified public/protected/internal members
- Code reviews will reject changes that lack proper documentation

**Examples of what MUST be documented:**

```csharp
// ✅ REQUIRED - Internal class must be documented
/// <summary>
/// Provides utility methods for generating unique names in test scenarios.
/// </summary>
internal static class SharedUtilities
{
    // ✅ REQUIRED - Internal method must be documented
    /// <summary>
    /// Generates a unique test name by appending a GUID to the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to use for the test name.</param>
    /// <returns>A unique test name.</returns>
    internal static string GetUniqueTestName(string prefix = "test") => $"{prefix}-{Guid.NewGuid():N}";
}

// ✅ REQUIRED - Protected method must be documented
/// <summary>
/// Creates a test subject for the specific implementation being tested.
/// </summary>
/// <param name="innerCache">The underlying cache to wrap.</param>
/// <returns>A new test subject instance.</returns>
protected abstract TTestSubject CreateTestSubject(IMemoryCache? innerCache = null);
```

**Violation Consequences:**

- Build failures due to CS1591 errors
- Code review rejection
- Potential revert of commits lacking proper documentation

---

## #region Usage Policy (Non-Negotiable)

**NEVER use `#region` in any code written for this repository.**

- The use of `#region` is strictly prohibited.
- `#region` is considered a code smell and a sign of multiple responsibilities or unnecessary complication.
- If you encounter code that uses `#region`, refactor to eliminate it by improving structure, cohesion, and clarity.
- All agents and contributors must avoid introducing `#region` in any new or modified code.

---
