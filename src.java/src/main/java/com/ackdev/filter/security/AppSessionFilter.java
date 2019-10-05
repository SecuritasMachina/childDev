package com.ackdev.filter.security;

import java.io.IOException;
import java.util.logging.Logger;

import javax.servlet.FilterChain;
import javax.servlet.ServletException;
import javax.servlet.ServletRequest;
import javax.servlet.ServletResponse;
import javax.servlet.annotation.WebFilter;
import javax.servlet.http.HttpServletRequest;
import javax.servlet.http.HttpServletResponse;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.stereotype.Component;
import org.springframework.web.context.support.SpringBeanAutowiringSupport;
import org.springframework.web.filter.GenericFilterBean;

import com.ackdev.beans.AppSecurityBean;
import com.ackdev.store.UserStore;
import com.common.security.dto.UserSession;
@SpringBootApplication
@Component
@WebFilter(filterName = "myFilter", urlPatterns = "/*")
//@Component
//@Order(1)
public class AppSessionFilter extends GenericFilterBean {

	@Autowired
	private UserStore requestUserStore;

	private static final Logger LOG = Logger.getLogger(AppSessionFilter.class.getName());

	@Override
	public void doFilter(ServletRequest pReq, ServletResponse pResp, FilterChain chain)
			throws IOException, ServletException {
		//SpringBeanAutowiringSupport.processInjectionBasedOnCurrentContext(this);
		SpringBeanAutowiringSupport.processInjectionBasedOnServletContext(this, pReq.getServletContext());
		HttpServletRequest req = (HttpServletRequest) pReq;
		HttpServletResponse resp = (HttpServletResponse) pResp;

		String reqUri = req.getRequestURI();

//TODO Check origin IP
		LOG.info("doFilter processing " + req.getMethod() + " " + reqUri);
		boolean isAllowed = false;
		if (reqUri.contains("/rest/system/purge") || reqUri.contains("/notification/postHandler")
				|| reqUri.contains("/_ah/bounce") || reqUri.contains("/rest/jobs/job/resource/deleteTask/")
				|| req.getMethod().equalsIgnoreCase("options")) {
			LOG.info("Ignore " + req.getMethod() + " " + reqUri);
			isAllowed = true;
		} else {
			UserSession sUserSession = AppSecurityBean.getInstance().getAndPopulateSession(req, resp);
			if (reqUri.contains("/secure/")) {
				if (sUserSession != null && sUserSession.currentAccount != null) {
					this.requestUserStore.setUserAccount(sUserSession.currentAccount);
					LOG.info("doFilter populated userStore: "+this.requestUserStore);
					if (sUserSession.currentAccount.authHash != null
							&& sUserSession.currentAccount.authHash.length() > 10) {
						isAllowed = true;
					}
				} else {
					LOG.info("sUserSession.currentAccount is null");
				}
			} else {
				isAllowed = true;
			}
		}
		if (isAllowed) {
			chain.doFilter(pReq, pResp);
		} else {
			resp.setStatus(401);
			resp.sendError(HttpServletResponse.SC_UNAUTHORIZED);
			return;

		}
	}

	@Override
	public void destroy() {
		// add code to release any resource
	}

}
