using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

// ReSharper disable all

#pragma warning disable CS0067 // event never used

namespace XMLDoc2Markdown.TestFixtures
{
    // ─── Records (#39) ───────────────────────────────────────────────────────────

    /// <summary>A simple record class with auto-properties.</summary>
    /// <param name="Id">Unique identifier.</param>
    /// <param name="Name">Display name.</param>
    public record SimpleRecord(Guid Id, string? Name);

    /// <summary>A record struct.</summary>
    /// <param name="X">X coordinate.</param>
    /// <param name="Y">Y coordinate.</param>
    public record struct Point(double X, double Y);

    /// <summary>A normal class that should still say "class".</summary>
    public class PlainClass { }

    // ─── Inheritance / inheritdoc (#41) ──────────────────────────────────────────

    /// <summary>Base interface for shape things.</summary>
    public interface IShape
    {
        /// <summary>Compute the area of the shape.</summary>
        /// <returns>A non-negative double representing the area.</returns>
        double Area();
    }

    /// <summary>Abstract base shape.</summary>
    public abstract class BaseShape : IShape
    {
        /// <summary>Common name of the shape.</summary>
        public abstract string Name { get; }

        /// <inheritdoc/>
        public abstract double Area();
    }

    /// <summary>A concrete circle shape.</summary>
    public class Circle : BaseShape
    {
        /// <summary>Radius of the circle.</summary>
        public double Radius { get; }

        /// <inheritdoc/>
        public override string Name => "Circle";

        /// <inheritdoc/>
        public override double Area() => Math.PI * Radius * Radius;
    }

    /// <summary>Demonstrates inheritdoc with an explicit cref.</summary>
    public class ExplicitInheritDocClass
    {
        /// <inheritdoc cref="IShape.Area"/>
        public double ComputeArea() => 0.0;
    }

    // ─── Generic / nested type links (#31) ───────────────────────────────────────

    /// <summary>A generic wrapper type.</summary>
    /// <typeparam name="T">The wrapped type.</typeparam>
    public class Wrapper<T>
    {
        /// <summary>The wrapped value.</summary>
        public T? Value { get; init; }

        /// <summary>Returns another wrapper around <see cref="List{T}"/>.</summary>
        /// <returns>A <see cref="Wrapper{T}"/> containing a <see cref="List{T}"/>.</returns>
        public Wrapper<List<T>> AsList() => new() { Value = Value is null ? null : new List<T> { Value } };
    }

    /// <summary>Uses a nullable property to exercise NullableAttribute rendering (#38).</summary>
    public class NullableHost
    {
        /// <summary>An optional value.</summary>
        public string? OptionalName { get; set; }
    }

    // ─── External cref (#42) ─────────────────────────────────────────────────────

    /// <summary>
    /// Exposes an execution context parameter.
    /// See <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> for the storage type.
    /// </summary>
    public class ExternalCrefClass
    {
        /// <summary>
        /// Input parameters dictionary — analogous to
        /// <see cref="System.Collections.Generic.IDictionary{TKey,TValue}"/>.
        /// </summary>
        public Dictionary<string, object>? InputParameters { get; set; }
    }

    // ─── Front matter (#36) ──────────────────────────────────────────────────────

    /// <summary>A type used to verify front-matter prepending.</summary>
    public class FrontMatterTarget { }

    // ─── Inline reference rendering (see langword / href / typeparamref) ─────────

    /// <summary>
    /// Demonstrates inline reference rendering.
    /// Pass <see langword="null"/> to opt out, or <see langword="true"/> to enable.
    /// See <see href="https://example.com/docs">external docs</see> for details.
    /// </summary>
    public class InlineRefHost
    {
        /// <summary>
        /// Returns <see langword="null"/> when no value is set.
        /// </summary>
        public string? NullableValue { get; set; }

        /// <summary>
        /// Processes a value of type <typeparamref name="T"/>.
        /// Pass <see langword="null"/> to use the default.
        /// More info at <see href="https://example.com/api">the API docs</see>.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="value">The value; may be <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if the value was accepted; <see langword="false"/> otherwise.</returns>
        public bool Process<T>(T? value) => value is not null;
    }

    // ─── Shared-framework constructor dependency ─────────────────────────────────

    /// <summary>Open generic result returned by a tool.</summary>
    /// <typeparam name="T">The success value.</typeparam>
    public sealed record OpenResult<T>(bool Success, T? Value)
    {
        /// <summary>Creates a successful result.</summary>
        /// <param name="value">The success value.</param>
        public static OpenResult<T> Ok(T value) => new(true, value);

        /// <summary>Creates a failed result.</summary>
        /// <param name="code">The failure code.</param>
        public static OpenResult<T> Fail(string code) => new(false, default);
    }

    /// <summary>Outer generic type.</summary>
    /// <typeparam name="T">The outer type parameter.</typeparam>
    public class OuterBox<T>
    {
        /// <summary>Nested generic type.</summary>
        /// <typeparam name="U">The inner type parameter.</typeparam>
        public class InnerBox<U>
        {
            /// <summary>Uses both enclosing type parameters.</summary>
            /// <param name="outer">The outer value.</param>
            /// <param name="inner">The inner value.</param>
            /// <param name="outerList">A list of outer values.</param>
            /// <param name="innerArray">An array of inner values.</param>
            public void Use(T outer, U inner, List<T> outerList, U[] innerArray)
            {
            }
        }

        /// <summary>Nested type with no type parameters of its own.</summary>
        public class PlainInner
        {
            /// <summary>Uses the outer type parameter.</summary>
            /// <param name="value">The outer value.</param>
            public void Use(T value)
            {
            }
        }
    }

    /// <summary>A type parameter and a method type parameter share a name.</summary>
    /// <typeparam name="T">The type parameter.</typeparam>
#pragma warning disable CS0693 // The shared name is the case under test.
    public class ShadowedBox<T>
    {
        /// <summary>The method type parameter hides the type parameter.</summary>
        /// <typeparam name="T">The method type parameter.</typeparam>
        /// <param name="value">A box of the method type parameter.</param>
        public void Accept<T>(ShadowedBox<T> value)
        {
        }
    }
#pragma warning restore CS0693

    /// <summary>A generic type with a generic method.</summary>
    /// <typeparam name="T">The type parameter.</typeparam>
    public class GenericMethodBox<T>
    {
        /// <summary>Accepts both the type parameter and a method type parameter.</summary>
        /// <typeparam name="U">The method type parameter.</typeparam>
        /// <param name="value">The type argument.</param>
        /// <param name="other">The method argument.</param>
        public void Accept<U>(T value, U other)
        {
        }
    }

    /// <summary>Depends on a logger supplied by the ASP.NET Core shared framework.</summary>
    public sealed class LoggingConsumer
    {
        /// <summary>Creates a consumer.</summary>
        /// <param name="logger">The logger.</param>
        public LoggingConsumer(ILogger<LoggingConsumer> logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
        }
    }
}
