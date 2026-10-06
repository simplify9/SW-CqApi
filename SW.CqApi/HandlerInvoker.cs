using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace SW.CqApi
{
    /// <summary>
    /// Calls a handler's <c>Handle</c> method through a delegate compiled once at startup,
    /// instead of <see cref="MethodInfo.Invoke(object, object[])"/> plus a reflective read of
    /// <c>Task&lt;T&gt;.Result</c> on every request.
    /// </summary>
    internal sealed class HandlerInvoker
    {
        private readonly Func<object, object[], Task> call;
        private readonly Func<Task, object> result;

        private HandlerInvoker(Func<object, object[], Task> call, Func<Task, object> result)
        {
            this.call = call;
            this.result = result;
        }

        public async Task<object> Invoke(object handler, params object[] arguments)
        {
            var task = call(handler, arguments);
            await task.ConfigureAwait(false);
            return result?.Invoke(task);
        }

        public static HandlerInvoker Create(MethodInfo method)
        {
            var instance = Expression.Parameter(typeof(object), "handler");
            var arguments = Expression.Parameter(typeof(object[]), "arguments");

            var parameters = method.GetParameters().Select((p, i) =>
                (Expression)Expression.Convert(
                    Expression.ArrayIndex(arguments, Expression.Constant(i)),
                    p.ParameterType));

            var body = Expression.Convert(
                Expression.Call(Expression.Convert(instance, method.DeclaringType), method, parameters),
                typeof(Task));

            var call = Expression.Lambda<Func<object, object[], Task>>(body, instance, arguments).Compile();

            Func<Task, object> result = null;
            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var task = Expression.Parameter(typeof(Task), "task");
                var read = Expression.Convert(
                    Expression.Property(Expression.Convert(task, method.ReturnType), "Result"),
                    typeof(object));
                result = Expression.Lambda<Func<Task, object>>(read, task).Compile();
            }

            return new HandlerInvoker(call, result);
        }
    }
}
