package org.operix.rpc;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;

/**
 * Line-delimited JSON-RPC server over stdin/stdout.
 *
 * Wire protocol — one JSON object per line in each direction.
 *
 * Request shapes:
 *   {"id":1, "class":"org.sikuli.script.Screen", "args":[]}
 *       -> construct an instance, returns {"id":1, "result":{"__ref":"o1"}}
 *
 *   {"id":2, "ref":"o1", "method":"click", "args":["btn.png"]}
 *       -> invoke method on a previously returned ref
 *
 *   {"id":3, "class":"org.sikuli.script.App", "method":"open",
 *    "args":["notepad"], "static":true}
 *       -> static method call
 *
 *   {"id":4, "ref":"o1", "release":true}
 *       -> drop the strong reference, allow GC
 *
 * Response shapes:
 *   {"id":N, "result": <json-value-or-{"__ref":"oK"}>}
 *   {"id":N, "error":  "stack trace string"}
 */
public final class Server {

    private final ObjectRegistry registry = new ObjectRegistry();
    private final Dispatcher dispatcher;
    private final PrintStream out;

    Server(PrintStream out) {
        this.out = out;
        this.dispatcher = new Dispatcher(registry);
    }

    public static void main(String[] args) throws IOException {
        // Anything OculiX prints to stdout would corrupt the JSON-RPC stream.
        // Re-route stdout to stderr; keep the original stdout for our protocol.
        PrintStream rpcOut = System.out;
        System.setOut(System.err);

        new Server(rpcOut).run(System.in);
    }

    void run(java.io.InputStream in) throws IOException {
        try (BufferedReader reader =
                     new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                line = line.trim();
                if (line.isEmpty()) continue;
                handleLine(line);
            }
        }
    }

    private void handleLine(String line) {
        JSONObject request;
        Object id = JSONObject.NULL;
        try {
            request = new JSONObject(line);
            id = request.opt("id");
            JSONObject response = dispatcher.dispatch(request);
            response.put("id", id);
            writeLine(response);
        } catch (JSONException e) {
            writeError(id, "Invalid JSON: " + e.getMessage());
        } catch (Throwable t) {
            writeError(id, t.toString());
        }
    }

    private void writeError(Object id, String message) {
        JSONObject err = new JSONObject();
        err.put("id", id == null ? JSONObject.NULL : id);
        err.put("error", message);
        writeLine(err);
    }

    private synchronized void writeLine(JSONObject obj) {
        out.println(obj.toString());
        out.flush();
    }

    static JSONArray emptyIfNull(JSONArray a) {
        return a == null ? new JSONArray() : a;
    }
}
