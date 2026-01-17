using System;
using System.Collections.Generic;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Value type operations interface.
    /// </summary>
    public interface IValueType<T>
    {
        /// <summary>
        /// Parses a value from a string.
        /// </summary>
        T Parse(string value);
        
        /// <summary>
        /// Mixes between two values.
        /// </summary>
        T Mix(T from, T to, float progress);
        
        /// <summary>
        /// Clamps a value between min and max.
        /// </summary>
        T Clamp(T value, T min, T max);
    }
    
    /// <summary>
    /// Registry for value type operations (parse, mix, clamp).
    /// Similar to motion-main's value type system.
    /// </summary>
    public static class ValueTypeRegistry
    {
        private static readonly Dictionary<Type, object> s_TypeHandlers = new Dictionary<Type, object>();
        
        static ValueTypeRegistry()
        {
            // Register default types
            Register<float>(new NumberValueType());
            Register<int>(new IntValueType());
        }
        
        /// <summary>
        /// Registers a value type handler.
        /// </summary>
        public static void Register<T>(IValueType<T> handler)
        {
            s_TypeHandlers[typeof(T)] = handler;
        }
        
        /// <summary>
        /// Gets a value type handler.
        /// </summary>
        public static IValueType<T> Get<T>()
        {
            if (s_TypeHandlers.TryGetValue(typeof(T), out var handler))
            {
                return (IValueType<T>)handler;
            }
            
            // Default: try to use number handler for numeric types
            if (typeof(T) == typeof(float) || typeof(T) == typeof(double) || 
                typeof(T) == typeof(int) || typeof(T) == typeof(long))
            {
                return (IValueType<T>)s_TypeHandlers[typeof(float)];
            }
            
            throw new NotSupportedException($"No value type handler registered for {typeof(T)}");
        }
        
        /// <summary>
        /// Mixes two values of the same type.
        /// </summary>
        public static T Mix<T>(T from, T to, float progress)
        {
            return Get<T>().Mix(from, to, progress);
        }
        
        /// <summary>
        /// Parses a value from a string.
        /// </summary>
        public static T Parse<T>(string value)
        {
            return Get<T>().Parse(value);
        }
        
        /// <summary>
        /// Clamps a value.
        /// </summary>
        public static T Clamp<T>(T value, T min, T max)
        {
            return Get<T>().Clamp(value, min, max);
        }
    }
    
    /// <summary>
    /// Number value type handler.
    /// </summary>
    internal class NumberValueType : IValueType<float>
    {
        public float Parse(string value)
        {
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, 
                System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }
            return 0.0f;
        }
        
        public float Mix(float from, float to, float progress)
        {
            return MathUtils.Mix(from, to, progress);
        }
        
        public float Clamp(float value, float min, float max)
        {
            return MathUtils.Clamp(value, min, max);
        }
    }
    
    /// <summary>
    /// Integer value type handler.
    /// </summary>
    internal class IntValueType : IValueType<int>
    {
        public int Parse(string value)
        {
            if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int result))
            {
                return result;
            }
            return 0;
        }
        
        public int Mix(int from, int to, float progress)
        {
            return (int)MathUtils.Mix(from, to, progress);
        }
        
        public int Clamp(int value, int min, int max)
        {
            return (int)MathUtils.Clamp(value, min, max);
        }
    }
}
