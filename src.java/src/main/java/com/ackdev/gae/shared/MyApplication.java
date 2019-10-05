package com.ackdev.gae.shared;

import java.util.logging.Logger;

import org.glassfish.jersey.server.ResourceConfig;

import com.ackdev.common.filter.ResponseCorsFilter;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;


public class MyApplication extends ResourceConfig {
    private static final Logger LOG = Logger.getLogger(MyApplication.class
            .getName());

    public MyApplication() {
        packages("com.ackdev.childDev.service");
        register(ResponseCorsFilter.class);
        //register(MultiPartFeature.class);
        LOG.info("ChildDev ResourceConfig v1.0.0");
        ObjectMapper objectMapper = new ObjectMapper();
        objectMapper.configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false);
    }
}
