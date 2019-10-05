package com.ackdev.utility;

import java.io.ByteArrayInputStream;
import java.net.URL;
import java.text.DecimalFormat;
import java.text.NumberFormat;
import java.util.ArrayList;
import java.util.List;
import java.util.logging.Logger;

import javax.xml.parsers.DocumentBuilder;
import javax.xml.parsers.DocumentBuilderFactory;

import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.Node;
import org.w3c.dom.NodeList;
import org.xml.sax.InputSource;

import com.google.appengine.api.urlfetch.FetchOptions;
import com.google.appengine.api.urlfetch.HTTPMethod;
import com.google.appengine.api.urlfetch.HTTPRequest;
import com.google.appengine.api.urlfetch.HTTPResponse;
import com.google.appengine.api.urlfetch.URLFetchService;
import com.google.appengine.api.urlfetch.URLFetchServiceFactory;

public class GoogleLocationWeather {
    private static final String URL_WEATHER_API = "http://www.google.com/ig/api?weather=,,,";

    private static Logger LOG = Logger.getLogger(GoogleLocationWeather.class
            .getName());

    private final transient FetchOptions fetchOptions = FetchOptions.Builder
            .withDeadline(20);

    private String mWeatherIcon;

    public String getWeatherIcon() {
        return mWeatherIcon;
    }

    public List<Sensor> requestForWeather(double latitude, double longitude) {
        List<Sensor> sensorList = new ArrayList<Sensor>();
        DecimalFormat decimalFormat = new DecimalFormat("#.######");
        latitude = Double.valueOf(decimalFormat.format(latitude)) * 1000000;
        longitude = Double.valueOf(decimalFormat.format(longitude)) * 1000000;

        byte[] ret = null;
        String sensorResponse = null;
        try {
            NumberFormat f = NumberFormat.getInstance();
            f.setGroupingUsed(false);
            String loc = f.format(latitude) + "," + f.format(longitude);
            loc = loc.replace(".", "");
            String urlStr = URL_WEATHER_API + loc;
            LOG.info("urlStr " + urlStr);
            final URL url = new URL(urlStr);
            final HTTPRequest vosaoReq = new HTTPRequest(url, HTTPMethod.GET,
                    this.fetchOptions);
            final URLFetchService service = URLFetchServiceFactory
                    .getURLFetchService();
            final HTTPResponse response = service.fetch(vosaoReq);
            ret = response.getContent();
            sensorResponse = new String(ret);
            LOG.info("sensorResponse " + sensorResponse);
            DocumentBuilderFactory factory = DocumentBuilderFactory
                    .newInstance();
            DocumentBuilder builder = factory.newDocumentBuilder();
            Document dom = builder.parse(new InputSource(new ByteArrayInputStream(sensorResponse.getBytes("utf-8"))));
            Element root = dom.getDocumentElement();
            NodeList items = root.getFirstChild().getChildNodes();
            Node item = items.item(1);
            if (item != null) {
                NodeList properties = item.getChildNodes();

                String humidity = properties.item(3).getAttributes()
                        .getNamedItem("data").getNodeValue()
                        .replace("Humidity: ", "").replace("%", "");
                mWeatherIcon = "http://www.google.com"
                        + properties.item(4).getAttributes()
                        .getNamedItem("data").getNodeValue();
                String windspeedMiles = properties.item(5).getAttributes()
                        .getNamedItem("data").getNodeValue()
                        .replace("Wind: ", "");
                String temp_C = properties.item(2).getAttributes()
                        .getNamedItem("data").getNodeValue();

                Sensor sensorHumidity = new Sensor();
                sensorHumidity.name = "humidity";
                sensorHumidity.nbrValue = Double.parseDouble(humidity);

                Sensor sensorWind = new Sensor();
                sensorWind.name = "windspeedMiles";
                sensorWind.strValue = windspeedMiles;

                Sensor sensorTemp = new Sensor();
                sensorTemp.name = "temp_C";
                sensorTemp.nbrValue = Double.parseDouble(temp_C);

                sensorList.add(sensorHumidity);
                sensorList.add(sensorWind);
                sensorList.add(sensorTemp);

                for (Sensor sensor : sensorList) {
                    LOG.info("sensor data " + sensor.name + " " + sensor.strValue + " " + sensor.nbrValue);
                }
            }
        } catch (Exception e) {
            LOG.info(e.toString());
        }

        return sensorList;
    }

    public void setWeatherIcon(String weatherIcon) {
        this.mWeatherIcon = weatherIcon;
    }
}
