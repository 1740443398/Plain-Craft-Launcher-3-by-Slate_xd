import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/**
 * PCL Java 插件示例。
 *
 * <p>通信协议：每行一条 JSON，stdin 接收启动器发来的，stdout 发回启动器。
 * 启动器只认识白名单里的 action，其余一律拒绝。</p>
 *
 * <p>编译：javac -encoding UTF-8 PclPlugin.java</p>
 * <p>打包：jar cfe plugin.jar PclPlugin PclPlugin.class</p>
 */
public class PclPlugin {

    private static String pluginId = "unknown";

    public static void main(String[] args) throws Exception {
        BufferedReader in = new BufferedReader(
                new InputStreamReader(System.in, StandardCharsets.UTF_8));
        PrintStream out = new PrintStream(System.out, true, "UTF-8");

        String line;
        while ((line = in.readLine()) != null) {
            line = line.trim();
            if (line.isEmpty()) continue;

            // 极简解析：只认 type / action / key / text，避免依赖第三方 JSON 库
            // 真实插件建议带上 Gson 或 Jackson
            String type = str(line, "type");

            if ("hello".equals(type)) {
                pluginId = str(line, "pluginId");
                log(out, "收到握手，插件 id = " + pluginId);
                // 演示：读启动器版本
                out.println("{\"action\":\"get_version\"}");
            } else if ("reply".equals(type)) {
                String action = str(line, "action");
                if ("get_version".equals(action)) {
                    log(out, "启动器版本 = " + str(line, "value"));
                    // 演示：弹个提示
                    out.println("{\"action\":\"toast\",\"text\":\"Java 插件已加载\"}");
                }
            } else if ("shutdown".equals(type)) {
                log(out, "收到关闭指令，退出");
                break;
            }
        }
    }

    private static void log(PrintStream out, String text) {
        out.println("{\"action\":\"log\",\"text\":\"" + esc(text) + "\"}");
    }

    /** 从 JSON 行里粗取某个键的字符串值。够用，但别拿它当通用解析器。 */
    private static String str(String json, String key) {
        String needle = "\"" + key + "\"";
        int i = json.indexOf(needle);
        if (i < 0) return "";
        int c = json.indexOf(':', i + needle.length());
        if (c < 0) return "";
        int q1 = json.indexOf('"', c + 1);
        if (q1 < 0) {
            // 非字符串值（数字/布尔）
            int end = c + 1;
            while (end < json.length() && ",}\n".indexOf(json.charAt(end)) < 0) end++;
            return json.substring(c + 1, end).trim();
        }
        StringBuilder sb = new StringBuilder();
        for (int p = q1 + 1; p < json.length(); p++) {
            char ch = json.charAt(p);
            if (ch == '\\' && p + 1 < json.length()) {
                char nx = json.charAt(++p);
                if (nx == 'n') sb.append('\n');
                else if (nx == '"') sb.append('"');
                else if (nx == '\\') sb.append('\\');
                else sb.append(nx);
            } else if (ch == '"') {
                break;
            } else {
                sb.append(ch);
            }
        }
        return sb.toString();
    }

    private static String esc(String s) {
        return s.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n");
    }
}
