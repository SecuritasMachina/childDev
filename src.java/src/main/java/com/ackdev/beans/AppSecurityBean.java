package com.ackdev.beans;

import java.util.logging.Logger;

import javax.servlet.ServletRequest;
import javax.servlet.ServletResponse;
import javax.servlet.http.HttpServletRequest;

import com.ackdev.common.utility.SessionCacheBean;
import com.common.security.beans.SecurityBean;
import com.common.security.dto.UserSession;
import com.common.security.jdo.UserAccountV2;

public class AppSecurityBean extends SecurityBean {
	private static final Logger LOG = Logger.getLogger(AppSecurityBean.class.getName());
	private volatile static AppSecurityBean mInstance;

	public static AppSecurityBean getInstance() {
		if (mInstance == null) {
			synchronized (AppSecurityBean.class) {

				if (mInstance == null) {
					mInstance = new AppSecurityBean();
				}
			}
		}
		return mInstance;
	}

	public UserSession getAndPopulateSession(ServletRequest pReq, ServletResponse pResp) {
		HttpServletRequest req = (HttpServletRequest) pReq;

		UserSession userSession = null;
		String authHash = req.getHeader("azAuthHeader");
		LOG.info("authHash:" + authHash);
		if (authHash != null) {
			userSession = (UserSession) SessionCacheBean.getInstance().get(authHash);
			if (userSession == null || userSession.currentAccount == null) {
				UserAccountV2 ua1 = this.lookupUserAccountByAuthHash(authHash);
				if (ua1 != null) {
					LOG.info("Found in lookupUserAccountByAuthHash " + ua1.email + " authHash:" + authHash);
					userSession = new UserSession(req);
					userSession.currentAccount = ua1;
				}
			} else {
				LOG.info("Found in SessionCacheBean " + userSession.currentAccount.email + " authHash:" + authHash);
			}
		}
		if (authHash != null && authHash.length() > 4)
			SessionCacheBean.getInstance().put(authHash, userSession);
		return userSession;
	}
}
