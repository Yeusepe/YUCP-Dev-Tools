using System;
using System.Collections.Generic;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Creates a MotionValue that transforms the output of other MotionValues.
    /// Similar to motion-main's transformValue/subscribeValue.
    /// </summary>
    public static class DerivedMotionValue
    {
        /// <summary>
        /// Creates a derived MotionValue that subscribes to input values and re-evaluates when they change.
        /// </summary>
        public static MotionValue<TOutput> Create<TOutput>(
            Func<TOutput> transform,
            params IMotionValue[] inputValues)
        {
            // Collect all MotionValues referenced in the transform
            var collectedValues = new List<IMotionValue>();
            if (inputValues != null && inputValues.Length > 0)
            {
                collectedValues.AddRange(inputValues);
            }
            
            // Evaluate initial value
            var initialValue = transform();
            var outputValue = new MotionValue<TOutput>(initialValue);
            
            // Subscribe to input changes
            SubscribeToInputs(collectedValues, outputValue, transform);
            
            return outputValue;
        }
        
        /// <summary>
        /// Subscribes output value to input value changes.
        /// </summary>
        private static void SubscribeToInputs<TOutput>(
            List<IMotionValue> inputValues,
            MotionValue<TOutput> outputValue,
            Func<TOutput> transform)
        {
            var subscriptions = new List<System.Func<bool>>();
            
            // Subscribe to each input value's change event
            foreach (var inputValue in inputValues)
            {
                if (inputValue is MotionValue<float> floatValue)
                {
                    var unsubscribe = floatValue.OnChange(_ => 
                    {
                        // Schedule update on next frame (would integrate with frame loop)
                        outputValue.Set(transform());
                    });
                    subscriptions.Add(unsubscribe);
                }
                // Add other type handlers as needed
            }
            
            // Clean up subscriptions when output is destroyed
            outputValue.On("destroy", _ =>
            {
                foreach (var unsubscribe in subscriptions)
                {
                    unsubscribe();
                }
            });
        }
    }
}
