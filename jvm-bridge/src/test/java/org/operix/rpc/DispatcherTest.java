package org.operix.rpc;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** Validates the JSON-RPC dispatch logic against a stub class that has nothing to do with OculiX. */
class DispatcherTest {

    private ObjectRegistry registry;
    private Dispatcher dispatcher;

    @BeforeEach
    void setup() {
        registry = new ObjectRegistry();
        dispatcher = new Dispatcher(registry);
    }

    // --- stub class used by the tests -----------------------------------------
    public static class Calculator {
        private int total;
        public Calculator() {}
        public Calculator(int initial) { this.total = initial; }
        public int add(int x) { total += x; return total; }
        public int multiply(int x, int y) { return x * y; }
        public static String greet(String name) { return "hello " + name; }
        public Calculator chain() { return this; }
        public Object nullable() { return null; }

        // Overloaded — same name, same arity, different param types
        public String describe(String s)  { return "string:" + s; }
        public String describe(int i)     { return "int:" + i; }
        public String describe(boolean b) { return "bool:" + b; }
    }

    @Test
    void constructor_no_args_returns_ref() throws Exception {
        JSONObject req = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("args", new JSONArray());
        JSONObject res = dispatcher.dispatch(req);
        JSONObject result = res.getJSONObject("result");
        assertTrue(result.has("__ref"));
        assertEquals(Calculator.class.getName(), result.getString("__class"));
    }

    @Test
    void constructor_with_int_arg_then_instance_call() throws Exception {
        JSONObject ctor = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("args", new JSONArray().put(10));
        String ref = dispatcher.dispatch(ctor).getJSONObject("result").getString("__ref");

        JSONObject add = new JSONObject()
                .put("ref", ref)
                .put("method", "add")
                .put("args", new JSONArray().put(5));
        assertEquals(15, dispatcher.dispatch(add).getInt("result"));

        JSONObject add2 = new JSONObject()
                .put("ref", ref)
                .put("method", "add")
                .put("args", new JSONArray().put(7));
        assertEquals(22, dispatcher.dispatch(add2).getInt("result"));
    }

    @Test
    void static_method_call_returns_string() throws Exception {
        JSONObject req = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("method", "greet")
                .put("static", true)
                .put("args", new JSONArray().put("world"));
        assertEquals("hello world", dispatcher.dispatch(req).getString("result"));
    }

    @Test
    void multi_arg_method_with_numeric_coercion() throws Exception {
        JSONObject ctor = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("args", new JSONArray());
        String ref = dispatcher.dispatch(ctor).getJSONObject("result").getString("__ref");

        // JSON only knows "Number" — ensure we coerce double -> int when the method needs ints
        JSONObject mul = new JSONObject()
                .put("ref", ref)
                .put("method", "multiply")
                .put("args", new JSONArray().put(6).put(7));
        assertEquals(42, dispatcher.dispatch(mul).getInt("result"));
    }

    @Test
    void object_returning_method_yields_ref_and_round_trip_works() throws Exception {
        JSONObject ctor = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("args", new JSONArray());
        String refA = dispatcher.dispatch(ctor).getJSONObject("result").getString("__ref");

        JSONObject chain = new JSONObject()
                .put("ref", refA)
                .put("method", "chain")
                .put("args", new JSONArray());
        JSONObject chainResult = dispatcher.dispatch(chain).getJSONObject("result");
        assertTrue(chainResult.has("__ref"));
        // Identity interning: same Java instance -> same ref id
        assertEquals(refA, chainResult.getString("__ref"));
    }

    @Test
    void null_return_is_encoded_as_json_null() throws Exception {
        JSONObject ctor = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("args", new JSONArray());
        String ref = dispatcher.dispatch(ctor).getJSONObject("result").getString("__ref");

        JSONObject req = new JSONObject()
                .put("ref", ref)
                .put("method", "nullable")
                .put("args", new JSONArray());
        assertEquals(JSONObject.NULL, dispatcher.dispatch(req).get("result"));
    }

    @Test
    void release_drops_the_reference() throws Exception {
        JSONObject ctor = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("args", new JSONArray());
        String ref = dispatcher.dispatch(ctor).getJSONObject("result").getString("__ref");

        dispatcher.dispatch(new JSONObject().put("ref", ref).put("release", true));

        assertThrows(IllegalArgumentException.class,
                () -> dispatcher.dispatch(new JSONObject().put("ref", ref).put("method", "add")
                        .put("args", new JSONArray().put(1))));
    }

    @Test
    void overload_resolution_by_arg_type() throws Exception {
        JSONObject ctor = new JSONObject()
                .put("class", Calculator.class.getName())
                .put("args", new JSONArray());
        String ref = dispatcher.dispatch(ctor).getJSONObject("result").getString("__ref");

        // String -> describe(String)
        assertEquals("string:hi", dispatcher.dispatch(new JSONObject()
                .put("ref", ref).put("method", "describe")
                .put("args", new JSONArray().put("hi"))).getString("result"));

        // Int -> describe(int)
        assertEquals("int:42", dispatcher.dispatch(new JSONObject()
                .put("ref", ref).put("method", "describe")
                .put("args", new JSONArray().put(42))).getString("result"));

        // Bool -> describe(boolean)
        assertEquals("bool:true", dispatcher.dispatch(new JSONObject()
                .put("ref", ref).put("method", "describe")
                .put("args", new JSONArray().put(true))).getString("result"));
    }

    @Test
    void unknown_class_raises() {
        JSONObject req = new JSONObject()
                .put("class", "no.such.Class")
                .put("args", new JSONArray());
        assertThrows(ClassNotFoundException.class, () -> dispatcher.dispatch(req));
    }
}
