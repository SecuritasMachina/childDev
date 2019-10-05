package com.ackdev.childDev.application;

import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.context.annotation.ComponentScan;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.config.annotation.EnableWebMvc;

@SpringBootApplication
@Configuration
@EnableWebMvc
@ComponentScan(basePackages = { "com.ackdev.childDev", "com.ackdev.store", "com.ackdev.filter.security" })
public class AppConfig {
	//private static final Logger LOG = Logger.getLogger(AppConfig.class.getName());

}