package com.ackdev.utility;

import java.lang.reflect.Type;
import java.security.MessageDigest;
import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Enumeration;
import java.util.UUID;

import javax.servlet.ServletRequest;

import com.ackdev.statics.forms.AppConstants;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonDeserializationContext;
import com.google.gson.JsonDeserializer;
import com.google.gson.JsonElement;
import com.google.gson.JsonParseException;
import com.google.gson.JsonPrimitive;
import com.google.gson.JsonSerializationContext;
import com.google.gson.JsonSerializer;

public class MyUtility {
    private static MessageDigest mDigestMD5;

    public static String makeMD5Hash(Long pToHash) {
        return makeMD5Hash(Long.toString(pToHash));
    }

    public static String makeMD5Hash(String pToHash) {
        try {
            if (mDigestMD5 == null)
                mDigestMD5 = MessageDigest.getInstance("MD5");
            byte[] hashedBytes = mDigestMD5.digest(pToHash.getBytes("UTF-8"));

            return convertByteArrayToHexString(hashedBytes);
        } catch (Exception ex) {
            ex.printStackTrace();
        }
        return null;
    }

    public static String makeUUID() {
        return UUID.randomUUID().toString().replaceAll("-", "");
    }
    public static String getSignature(Object toJson) {
        if(toJson!=null) {
            String toSign = getGsonObj().toJson(toJson);
            return makeMD5Hash(toSign);
        }
       return "un-signed null";
    }

    private static String convertByteArrayToHexString(byte[] arrayBytes) {
        StringBuffer stringBuffer = new StringBuffer();
        for (int i = 0; i < arrayBytes.length; i++) {
            stringBuffer.append(Integer.toString((arrayBytes[i] & 0xff) + 0x100, 16)
                    .substring(1));
        }
        return stringBuffer.toString();
    }

    public static Boolean convertStringToBoolean(String billable) {
        Boolean ret = null;
        try {
            ret = Boolean.valueOf(billable);
        } catch (Exception e) {
        }
        return ret;

    }

    public static Double convertStringToDouble(String in) {
        Double ret = null;
        try {
            ret = Double.valueOf(in);
        } catch (Exception e) {
        }
        return ret;
    }

    public static Integer convertStringToInt(String in) {
        Integer ret = null;
        try {
            ret = Integer.valueOf(in);
            if (ret.longValue() == -1) {
                ret = null;
            }

        } catch (Exception e) {
        }
        return ret;
    }

    public static Long convertStringToLong(String in) {
        Long ret = null;
        try {
            ret = Long.valueOf(in);
            if (ret.longValue() == -1) {
                ret = null;
            }

        } catch (Exception e) {
        }
        return ret;
    }

    public static void dumpRequest(final ServletRequest request) {
        Enumeration<?> enumer = request.getAttributeNames();
        String log = "getAttribute\n";
        while (enumer.hasMoreElements()) {
            String key = (String) enumer.nextElement();

            try {
                Object value = request.getAttribute(key);
                log += key + " : " + value.toString() + "\n";

            } catch (Exception e) {
                // TODO Auto-generated catch block
                e.printStackTrace();
            }
        }
        log = "getParameterNames\n";
        enumer = request.getParameterNames();
        while (enumer.hasMoreElements()) {
            String key = (String) enumer.nextElement();

            try {
                Object value = request.getParameter(key);
                log += key + " : " + value.toString() + "\n";
            } catch (Exception e) {
                // TODO Auto-generated catch block
                e.printStackTrace();
            }
        }
    }

    public static Gson getGsonObj() {
        Gson ret = null;
        final GsonBuilder builder = new GsonBuilder();
        builder.registerTypeAdapter(Date.class, new JsonDeserializer<Date>() {

            @Override
            public Date deserialize(final JsonElement json, final Type typeOfT,
                                    final JsonDeserializationContext context)
                    throws JsonParseException {
                final SimpleDateFormat format = new SimpleDateFormat(
                        AppConstants.SERVER_TIME_FORMAT);
                final String date = json.getAsJsonPrimitive().getAsString();
                try {
                    return format.parse(date);
                } catch (final ParseException ignore) {
                    // throw new RuntimeException(e);
                }
                return null;
            }

        });
        builder.registerTypeAdapter(Date.class, new JsonSerializer<Date>() {
            @Override
            public JsonElement serialize(final Date src, final Type typeOfSrc,
                                         final JsonSerializationContext context) {
                final SimpleDateFormat format = new SimpleDateFormat(
                        AppConstants.SERVER_TIME_FORMAT);
                final String date = format.format(src);
                return src == null ? null : new JsonPrimitive(date);
            }

        });
        ret = builder.create();
        return ret;
    }

}
