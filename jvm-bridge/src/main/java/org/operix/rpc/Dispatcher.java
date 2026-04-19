package org.operix.rpc;

import org.json.JSONArray;
import org.json.JSONObject;

import java.lang.reflect.Constructor;
import java.lang.reflect.Method;

/**
 * Routes a single JSON-RPC request to a constructor / static method /
 * instance method via reflection, then encodes the return value back to
 * JSON.
 *
 * Lookup is by name + arity (no overload resolution by argument types yet —
 * if a class has multiple methods with the same name and arity we pick the
 * first one Java reflection returns, which is good enough for the OculiX
 * surface area).
 */
final class Dispatcher {

    private final ObjectRegistry registry;

    Dispatcher(ObjectRegistry registry) {
        this.registry = registry;
    }

    JSONObject dispatch(JSONObject req) throws Exception {
        // Release flow: drop a ref, no return value.
        if (req.optBoolean("release", false)) {
            registry.release(req.getString("ref"));
            return new JSONObject().put("result", JSONObject.NULL);
        }

        String methodName = req.optString("method", null);
        JSONArray jsonArgs = Server.emptyIfNull(req.optJSONArray("args"));
        Object[] args = decodeArgs(jsonArgs);

        Object result;

        if (req.has("ref")) {
            // Instance method call
            Object target = registry.get(req.getString("ref"));
            Method m = findMethod(target.getClass(), methodName, args.length, args);
            result = m.invoke(target, coerce(m, args));
        } else if (req.has("class")) {
            String className = req.getString("class");
            Class<?> klass = Class.forName(className);
            if (methodName == null) {
                // Constructor call
                Constructor<?> c = findConstructor(klass, args.length, args);
                result = c.newInstance(coerce(c.getParameterTypes(), args));
            } else {
                // Static method call
                Method m = findMethod(klass, methodName, args.length, args);
                result = m.invoke(null, coerce(m, args));
            }
        } else {
            throw new IllegalArgumentException("Request needs either 'ref' or 'class'");
        }

        return new JSONObject().put("result", encode(result));
    }

    // --- reflection helpers ----------------------------------------------------

    /**
     * Pick the best method for {@code (name, arity, args)}. Scores each
     * candidate by how well its formal parameter types match the runtime
     * argument types — higher is better.
     */
    private static Method findMethod(Class<?> klass, String name, int arity, Object[] args) {
        // First pass: prefer non-bridge declared methods.
        Method best = pickByName(klass, name, arity, args, false);
        if (best != null) return best;
        // Fallback: include bridge methods (e.g. CharSequence.length() exposed
        // via bridges on StringBuilder).
        best = pickByName(klass, name, arity, args, true);
        if (best != null) return best;
        throw new NoSuchElementException("No method " + klass.getName() + "." + name
                + " with arity " + arity);
    }

    private static Method pickByName(Class<?> klass, String name, int arity,
                                     Object[] args, boolean allowBridge) {
        Method best = null;
        int bestScore = -1;
        for (Method m : klass.getMethods()) {
            if (!m.getName().equals(name)) continue;
            if (m.getParameterCount() != arity) continue;
            if (!allowBridge && (m.isBridge() || m.isSynthetic())) continue;
            int score = score(m.getParameterTypes(), args);
            if (score > bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    private static Constructor<?> findConstructor(Class<?> klass, int arity, Object[] args) {
        Constructor<?> best = null;
        int bestScore = -1;
        for (Constructor<?> c : klass.getConstructors()) {
            if (c.getParameterCount() != arity) continue;
            int score = score(c.getParameterTypes(), args);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        if (best == null) {
            throw new NoSuchElementException("No constructor for " + klass.getName()
                    + " with arity " + arity);
        }
        return best;
    }

    /**
     * Match score for the whole arg list. Higher is better, -1 means at least
     * one arg is incompatible.
     */
    private static int score(Class<?>[] formals, Object[] args) {
        int total = 0;
        for (int i = 0; i < formals.length; i++) {
            int s = scoreOne(formals[i], args[i]);
            if (s < 0) return -1;
            total += s;
        }
        return total;
    }

    /**
     * Per-arg score:
     *   3 = natural fit (Double -> double/Double, Integer -> int/Integer, etc.)
     *   2 = same numeric family with widening (Integer -> long, Float -> double)
     *   1 = cross-family numeric coercion that loses information
     *       (Double -> int, Integer -> double when no better candidate exists)
     *   0 = JSON null in a non-primitive slot
     *  -1 = incompatible
     *
     * Without this fine-grained ranking we'd pick {@code Math.max(int,int)}
     * over {@code Math.max(double,double)} when given JSON 3.5.
     */
    private static int scoreOne(Class<?> formal, Object arg) {
        if (arg == null) return formal.isPrimitive() ? -1 : 1;
        Class<?> a = arg.getClass();

        if (formal.isAssignableFrom(a)) return 3;

        if (arg instanceof Number) {
            // Integer family — small whole numbers
            if (a == Integer.class || a == Short.class || a == Byte.class) {
                if (formal == int.class    || formal == Integer.class)  return 3;
                if (formal == long.class   || formal == Long.class)     return 2;
                if (formal == short.class  || formal == Short.class)    return 2;
                if (formal == byte.class   || formal == Byte.class)     return 2;
                if (formal == double.class || formal == Double.class)   return 2;
                if (formal == float.class  || formal == Float.class)    return 2;
            }
            // Long family
            if (a == Long.class) {
                if (formal == long.class   || formal == Long.class)     return 3;
                if (formal == double.class || formal == Double.class)   return 2;
                if (formal == float.class  || formal == Float.class)    return 1;
                if (formal == int.class    || formal == Integer.class)  return 1;
            }
            // Double family — fractional values
            if (a == Double.class || a == java.math.BigDecimal.class) {
                if (formal == double.class || formal == Double.class)   return 3;
                if (formal == float.class  || formal == Float.class)    return 2;
                if (formal == long.class   || formal == Long.class)     return 1;
                if (formal == int.class    || formal == Integer.class)  return 1;
            }
            // Float family
            if (a == Float.class) {
                if (formal == float.class  || formal == Float.class)    return 3;
                if (formal == double.class || formal == Double.class)   return 2;
            }
        }

        if (arg instanceof Boolean && (formal == boolean.class || formal == Boolean.class)) return 3;

        return -1;
    }

    // --- value encoding/decoding ----------------------------------------------

    private Object[] decodeArgs(JSONArray a) {
        Object[] out = new Object[a.length()];
        for (int i = 0; i < a.length(); i++) out[i] = decode(a.get(i));
        return out;
    }

    private Object decode(Object v) {
        if (v == JSONObject.NULL) return null;
        if (v instanceof JSONObject) {
            JSONObject obj = (JSONObject) v;
            if (obj.has("__ref")) return registry.get(obj.getString("__ref"));
        }
        return v; // primitives + Strings pass through
    }

    private Object encode(Object v) {
        if (v == null) return JSONObject.NULL;
        Class<?> c = v.getClass();
        if (v instanceof String || v instanceof Number || v instanceof Boolean) return v;
        if (c.isPrimitive()) return v;
        // Anything else becomes an opaque ref
        String id = registry.register(v);
        return new JSONObject().put("__ref", id).put("__class", c.getName());
    }

    /** Adapt args to a Method's parameter types (covers boxing/numeric widening). */
    private static Object[] coerce(Method m, Object[] args) {
        return coerce(m.getParameterTypes(), args);
    }

    private static Object[] coerce(Class<?>[] types, Object[] args) {
        Object[] out = new Object[args.length];
        for (int i = 0; i < args.length; i++) out[i] = coerceOne(types[i], args[i]);
        return out;
    }

    private static Object coerceOne(Class<?> target, Object v) {
        if (v == null) return null;
        if (target.isInstance(v)) return v;
        if (v instanceof Number) {
            Number n = (Number) v;
            if (target == int.class    || target == Integer.class) return n.intValue();
            if (target == long.class   || target == Long.class)    return n.longValue();
            if (target == double.class || target == Double.class)  return n.doubleValue();
            if (target == float.class  || target == Float.class)   return n.floatValue();
            if (target == short.class  || target == Short.class)   return n.shortValue();
            if (target == byte.class   || target == Byte.class)    return n.byteValue();
        }
        return v;
    }

    /** Mirrors java.util.NoSuchElementException without forcing a verbose import in callers. */
    static final class NoSuchElementException extends RuntimeException {
        NoSuchElementException(String msg) { super(msg); }
    }
}
