package com.ackdev.beans.accounting;

import static com.ackdev.childDev.application.LocalOfyService.ofy;
import static com.ackdev.common.application.CommonOfyService.ofyCommon;

import java.io.IOException;
import java.util.List;
import java.util.Random;
import java.util.logging.Level;
import java.util.logging.Logger;

import javax.ws.rs.WebApplicationException;

import com.ackdev.common.dto.CommonResponse;
import com.ackdev.common.utility.MyUtility;
import com.ackdev.jdo.security.History;
import com.common.security.beans.SecurityBean;
import com.common.security.dto.LogonDTO;
import com.common.security.dto.RegisterUserDTO;
import com.common.security.dto.UserSession;
import com.common.security.jdo.UserAccountV2;
import com.google.gson.Gson;

public class AccountBean {

	private static Logger LOG = Logger.getLogger(AccountBean.class.getName());
	private volatile static AccountBean mInstance;
	private SecurityBean mSecurityBean = SecurityBean.getInstance();
	private Gson mGson = new Gson();
	private Random mRand = new Random();

	public static AccountBean getInstance() {
		if (mInstance == null) {
			synchronized (AccountBean.class) {

				if (mInstance == null) {
					mInstance = new AccountBean();
				}
			}
		}
		return mInstance;
	}

	public UserAccountV2 doGetSessionAccount(UserSession sUserSession) {
		UserAccountV2 ret = ofyCommon().load().type(UserAccountV2.class).id(sUserSession.currentAccount.guid).now();
		this.fixTimeZone(ret);

		return ret;
	}

	public void fixTimeZone(UserAccountV2 pAccount) {
		if (pAccount != null) {
			String defTZ = "Asia/Anadyr";
			boolean doSave = false;
			try {
				if (pAccount.timeZoneName == null || pAccount.timeZoneName.trim().length() < 2) {
					pAccount.timeZoneName = defTZ;
					LOG.info(pAccount.email + " missing TZ, used " + pAccount.timeZoneName);
					doSave = true;
				}
				if (pAccount.guid == null) {
					doSave = true;
					pAccount.guid = MyUtility.makeUUID();
				}
				if (doSave) {
					ofyCommon().save().entities(pAccount);
				}
			} catch (Exception e) {
				LOG.log(Level.SEVERE, e.getMessage(), e);
			}
		}
	}

	public UserAccountV2 doRegisterUser(UserSession sUserSession, RegisterUserDTO pRegisterUserDTO) {
		pRegisterUserDTO.isPrimaryContact = true;
//        if (pRegisterUserDTO.companyAccountGuid == null)
//            pRegisterUserDTO.companyAccountGuid = this.setupCompany(pRegisterUserDTO);
		UserAccountV2 userAccount = mSecurityBean.doRegisterUser(sUserSession.ip, pRegisterUserDTO);

		if (userAccount != null) {
			LogonDTO logonDTO = new LogonDTO(pRegisterUserDTO.email, pRegisterUserDTO.password);
			UserAccountV2 userAccountT = mSecurityBean.doLogon(sUserSession, logonDTO);
			if (userAccountT != null) {
				LOG.warning("userAccountT is null" + mGson.toJson(logonDTO));
				userAccount = userAccountT;
			}
//            userAccount.onLoad();
			fixTimeZone(userAccount);
			sUserSession.currentAccount = mGson.fromJson(mGson.toJson(userAccount), UserAccountV2.class);

		} else {
			LOG.severe("Tried to reregister pRegisterUserDTO:" + mGson.toJson(pRegisterUserDTO));
		}
		return userAccount;
	}

	public UserAccountV2 getByGuid(String pId) {

		return (get(pId, false));
	}

	public UserAccountV2 get(String pId, boolean bGetDeleted) {
		UserAccountV2 ret = null;
		try {
			ret = ofyCommon().load().type(UserAccountV2.class).id(pId).now();
			if (!bGetDeleted) {

//TODO
			}
		} catch (Exception e) {
		}
		return ret;
	}

	public CommonResponse.Status update(UserSession sUserSession, UserAccountV2 pAccountEntity)
			throws IOException, WebApplicationException {
		CommonResponse.Status ret = new CommonResponse.Status().ok();

		if (pAccountEntity.guid == null)
			pAccountEntity.guid = MyUtility.makeUUID();

		if (pAccountEntity.companyAccountGuidFk == null && sUserSession != null
				&& sUserSession.currentAccount != null) {
			pAccountEntity.companyAccountGuidFk = sUserSession.currentAccount.companyAccountGuidFk;
		} else {
			// currentAccount
			// Check if parent is owner, if not abort, notify email of
			// hacker,
			// add ip to filter out for 15 minutes
			if (sUserSession != null && sUserSession.currentAccount != null
					&& pAccountEntity.companyAccountGuidFk != null
					&& !pAccountEntity.companyAccountGuidFk.equals(sUserSession.currentAccount.companyAccountGuidFk)) {
				// TODO add to ehcache or google master exclude list or something..
			}
		}


		if (pAccountEntity.timeZoneName == null || pAccountEntity.timeZoneName.equalsIgnoreCase("Asia/Anadyr")) {
			if (sUserSession != null && sUserSession.currentAccount != null)
				pAccountEntity.timeZoneName = sUserSession.currentAccount.timeZoneName;
		}

		ofyCommon().save().entity(pAccountEntity).now();
		ret.ok("Saved");
		ret.guid = pAccountEntity.guid;
		return ret;
	}

	public String purgeUserData(String pEmail) {
		List<UserAccountV2> aUserAccount = ofyCommon().load().type(UserAccountV2.class).filter("email", pEmail).list();
		int tCount = aUserAccount.size();
		if (aUserAccount != null)
			ofyCommon().delete().entities(aUserAccount);
		//TODO Delete goals, journals
		
		return "deleted " + tCount + " " + pEmail;
	}

	public CommonResponse.Status removeAccount(UserSession sUserSession, String guid) {
		UserAccountV2 aUserAccount = ofyCommon().load().type(UserAccountV2.class).id(guid).now();
		CommonResponse.Status r = new CommonResponse.Status().ok();

		if (aUserAccount != null) {
			aUserAccount.expirationDate = System.currentTimeMillis();
			aUserAccount.updatedOn = System.currentTimeMillis();
			ofyCommon().save().entities(aUserAccount).now();
			UserAccountV2 e2 = this.get(guid, true);
			if (e2 != null && e2.expirationDate != null) {
				ofy().save()
						.entity(new History("DELETE", sUserSession.currentAccount.email, aUserAccount.email, 365 * 7));

				r.ok("Soft deleted " + guid + " " + e2.email + " " + e2.expirationDate.toString());
			} else {
				r.fail("unable to delete " + guid + " " + aUserAccount.email);
			}
		} else {
			r.fail("unable to delete " + guid + " " + sUserSession.currentAccount.email);
		}
		LOG.info("removeAccount: " + r.getStatus() + " " + r.getMessage() + " "
				+ MyUtility.getGsonObj().toJson(aUserAccount));
		return r;

	}

}
