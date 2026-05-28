using System;
using System.Collections.Generic;

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
}
