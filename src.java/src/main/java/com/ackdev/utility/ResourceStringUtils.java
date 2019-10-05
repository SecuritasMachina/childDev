package com.ackdev.utility;

import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import java.util.logging.Level;
import java.util.logging.Logger;

import org.apache.commons.io.IOUtils;

import com.ackdev.common.utility.HttpUtil;

public class ResourceStringUtils {
    static List<String> nameList = new ArrayList<String>();
    static List<String> partList = new ArrayList<String>();
    static List<String> colorList = new ArrayList<String>();
    private volatile static ResourceStringUtils mInstance;
    private static Logger LOG = Logger.getLogger(ResourceStringUtils.class
            .getName());
    private List<String> tagList = new ArrayList<String>();
    private Random ran = new Random();

    public static ResourceStringUtils getInstance() {
        if (mInstance == null) {
            synchronized (ResourceStringUtils.class) {

                if (mInstance == null) {
                    mInstance = new ResourceStringUtils();
                }
            }
        }
        return mInstance;
    }

    public String getRandomColor() {
        String retVal = "";
        populateColorArray();
        int tCount = 0;
        int x = 0;
        while (x < 0 && tCount < 20) {
            x = ran.nextInt(colorList.size() - 1);
            tCount++;
        }
        retVal = colorList.get(x).trim();
        return retVal;
    }

    public String getFirstName() {
        String retVal = "";
        populateNameArray();
        int x = ran.nextInt(49);
        String name = nameList.get(x);
        retVal = name.substring(0, name.indexOf(" ")).trim();
        return retVal;
    }

    public String getLastName() {
        String retVal = "";
        populateNameArray();
        int x = ran.nextInt(49);
        String name = nameList.get(x);
        retVal = name.substring(name.indexOf(" ")).trim();
        return retVal;
    }

    public String getRandomTags() {
        int tagCount = ran.nextInt(9);
        String retVal = "";
        populateTagArray();
        tagCount+=3;
        for (int t1 = 0; t1 < tagCount; t1++) {
            int x = ran.nextInt(tagList.size() - 1);
            String name = tagList.get(x);
            retVal += name + ",";
        }
        retVal = retVal.substring(0, retVal.length() - 1);
        return retVal;
    }

    public String getPartName() {
        String retVal = "";
        populatePartArray();
        int x = ran.nextInt(28);
        String name = partList.get(x);
        retVal = name.substring(name.indexOf(" ")).trim();
        return retVal;
    }

    public String getPartNbr() {
        String retVal = "";
        populatePartArray();
        int x = ran.nextInt(28);
        String name = partList.get(x);
        retVal = name.substring(0, name.indexOf(" ")).trim();
        return retVal;
    }

    public String getResourceAsString(String pUrl) {
        InputStream is = this.getClass().getResourceAsStream(pUrl);
        try {
            return IOUtils.toString(is, "UTF-8");
        } catch (IOException e) {
            e.printStackTrace();
        }
        return "Exception129";

    }

    private void populateColorArray() {
        if (colorList.size() == 0) {
            try {
                colorList = IOUtils.readLines(HttpUtil.getInstance().getResourceStream("colorList.txt"));

            } catch (IOException e) {
                LOG.log(Level.SEVERE, e.getMessage(), e);
            }
        }
    }

    private void populateTagArray() {
        if (tagList.size() == 0) {
            try {
                tagList = IOUtils.readLines(HttpUtil.getInstance().getResourceStream("tagList.txt"));

            } catch (IOException e) {
                LOG.log(Level.SEVERE, e.getMessage(), e);
            }

        }

    }

    private void populateNameArray() {
        if (nameList.size() == 0) {
            try {
                //String useUrl = "http://" + ConfigSystemBean.getInstance().getVal(ConfigSystemBean.Item.WEB_SERVER) + "/velocityTemplates";
                nameList = IOUtils.readLines(HttpUtil.getInstance().getResourceStream("nameList.txt"));

            } catch (IOException e) {
                LOG.log(Level.SEVERE, e.getMessage(), e);
            }

        }

    }

    private void populatePartArray() {
        if (partList.size() == 0) {
            try {
                partList = IOUtils.readLines(HttpUtil.getInstance().getResourceStream("partsList.txt"));

            } catch (IOException e) {
                LOG.log(Level.SEVERE, e.getMessage(), e);
            }

        }

    }
}
