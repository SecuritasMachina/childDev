package com.ackdev.store;

import java.io.Serializable;

import org.springframework.context.annotation.Scope;
import org.springframework.context.annotation.ScopedProxyMode;
import org.springframework.stereotype.Component;
import org.springframework.web.context.WebApplicationContext;

import com.common.security.jdo.UserAccountV2;
@Component
@Scope(value = WebApplicationContext.SCOPE_REQUEST, proxyMode = ScopedProxyMode.TARGET_CLASS)
public class UserStore implements Serializable{

	private static final long serialVersionUID = -855427530519105403L;
	private UserAccountV2 userAccount;

	public void clear() {
		this.setUserAccount(null);
	}

	public UserAccountV2 getUserAccount() {
		return userAccount;
	}

	public void setUserAccount(UserAccountV2 pUserAccount) {
		this.userAccount = pUserAccount;
	}
}
