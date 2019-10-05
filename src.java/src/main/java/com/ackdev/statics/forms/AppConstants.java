package com.ackdev.statics.forms;


public class AppConstants {
    public enum EnumMessages {
        delete("delete");

        public static EnumMessages fromString(String text) {
            if (text != null) {
                for (EnumMessages b : EnumMessages.values()) {
                    if (text.equalsIgnoreCase(b.text)) {
                        return b;
                    }
                }
            }
            return null;
        }

        private String text;

        EnumMessages(String text) {
            this.text = text;
        }

        public String getText() {
            return text;
        }

    }

    // public static final String SERVER_TIME_FORMAT="MMM d,yyyy h:mm:ss a";
    // public static final String SERVER_TIME_FORMAT="yyyy-MM-dd HH:mm:ss zzz";
    // public static final String SERVER_TIME_FORMAT="MMM d, yyyy h:mm:ss a";
    public static final String SERVER_TIME_FORMAT = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    public static final String VOSAO_RESOURCE_PATH_DEVICE = "/resources/situationware/";
    public static final String VOSAO_RESOURCE_PATH_DEVICES = "/devices/";
    public static final String VOSAO_RESOURCE_PATH_TEAMS = "/teams/";

    public static final String VOSAO_RESOURCE_PATH_DEVICE_FOLDER = "/resources/";

    public static final String DEFAULT_APPLICATION_TAG = "EmergencyResponsePlanner";
    public static final String RESOURCESERVERTOMCAT = "TOMCAT";

    public static final String RESOURCESERVERVOSAO = "VOSAO";
    public static final String GLOBALFEATUREFRIENDLYNAME = "Global Form Definition";
}
