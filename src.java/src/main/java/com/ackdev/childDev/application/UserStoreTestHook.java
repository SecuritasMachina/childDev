package com.ackdev.childDev.application;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import com.ackdev.store.UserStore;
import com.common.security.jdo.UserAccountV2;

@Service
public class UserStoreTestHook {
	
	@Autowired
	private UserStore mUserStore;

	public void setUserStoreAccount(UserAccountV2 pUserAccount) {
		mUserStore.setUserAccount(pUserAccount);
	}
}
